using FastExpressionCompiler;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using static System.Linq.Expressions.Expression;
using Workflows.Abstraction.Helpers;
using Workflows.Runner.Helpers;
using Workflows.Runner.DataObjects;

namespace Workflows.Runner.ExpressionTransformers;

/// <summary>
/// Transforms and analyzes match expressions to extract mandatory parts and prepare them for evaluation.
/// Converts lambda expressions with 1 parameter (signalData) to 3 parameters (signalData, workflowInstance, closure).
/// </summary>
internal partial class MatchExpressionWriter : ExpressionVisitor
{
    private LambdaExpression _matchExpression;
    private readonly object _currentWorkflowInstance;
    private readonly List<ExpressionPart> _expressionParts = new();

    /// <summary>
    /// Gets the analyzed and transformed match expression parts.
    /// </summary>
    internal MatchTransformationResult MatchTransformationResult { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MatchExpressionWriter"/> class.
    /// Processes the match expression to identify mandatory parts and transform the signature.
    /// </summary>
    /// <param name="matchExpression">The lambda expression to analyze and transform.</param>
    /// <param name="workflowInstance">The current workflow instance for closure resolution.</param>
    internal MatchExpressionWriter(LambdaExpression matchExpression, object workflowInstance)
    {
        MatchTransformationResult = new();
        _matchExpression = matchExpression;

        // Early exit if no expression to process
        if (_matchExpression == null)
            return;

        // If already transformed to 3 parameters, use as-is
        if (_matchExpression?.Parameters.Count == 3)
        {
            MatchTransformationResult.MatchExpression = _matchExpression;
            return;
        }

        _currentWorkflowInstance = workflowInstance;

        // Transform the expression through multiple stages
        ChangeSignature();           // Convert from 1 to 3 parameters
        FindExactMatchParts();       // Identify exact match comparisons
        MarkMandatoryParts();        // Determine which parts are mandatory for matching
        ReplaceLocalVariables();     // Replace closure variables with parameter references

        // Generate mandatory part expressions if any exist
        if (_expressionParts.Any(x => x.IsMandatory))
        {
            var mandatoryPartVisitor = new MandatoryPartExpressionsGenerator(_matchExpression, _expressionParts);
            MatchTransformationResult.SignalExactMatchPaths = mandatoryPartVisitor.SignalExactMatchPaths;
            MatchTransformationResult.InstanceExactMatchExpression = mandatoryPartVisitor.InstanceExactMatchExpression;
            SetIsMandatoryPartFullMatchValue();
        }
    }

    /// <summary>
    /// Replaces closure variable references with the closure parameter and captures the closure instance.
    /// </summary>
    private void ReplaceLocalVariables()
    {
        var changeClosureVarsVisitor = new GenericVisitor();
        Expression closure = null;

        // Find and replace compiler-generated closure constants with the closure parameter
        changeClosureVarsVisitor.OnVisitConstant(node =>
        {
            if (node.Type.Name.StartsWith(CompilerConstants.ClosurePrefix))
            {
                closure = node;
                // Replace with the 3rd parameter (closure)
                return _matchExpression.Parameters[2];
            }
            return base.VisitConstant(node);
        });

        MatchTransformationResult.MatchExpression = (LambdaExpression)changeClosureVarsVisitor.Visit(MatchTransformationResult.MatchExpression);

        // Capture the actual closure instance for later use
        if (closure != null)
        {
            var value = Lambda<Func<object>>(closure).CompileFast().Invoke();
            if (value != null)
                MatchTransformationResult.Closure = value;
        }
    }

    /// <summary>
    /// Transforms the expression signature from (signalData) to (signalData, workflowInstance, closure).
    /// </summary>
    private void ChangeSignature()
    {
        var expression = _matchExpression;

        // Create new parameters with proper types
        var signalDataArg = Parameter(_matchExpression.Parameters[0].Type, "signalData");
        var workflowInstanceArg = Parameter(_currentWorkflowInstance.GetType(), "workflowInstance");
        var closureType = GetClosureType();
        var localVarsArg = Parameter(closureType, "closure");

        // Visit the expression tree and replace old parameters with new ones
        var changeParameterVisitor = new GenericVisitor();
        changeParameterVisitor.OnVisitConstant(OnVisitConstant);
        changeParameterVisitor.OnVisitParameter(OnVisitParameter);
        var updatedBoy = changeParameterVisitor.Visit(_matchExpression.Body);

        // Build the new function type: Func<TSignalData, TInstance, TClosure, bool>
        var functionType = typeof(Func<,,,>)
            .MakeGenericType(
                signalDataArg.Type,
                _currentWorkflowInstance.GetType(),
                closureType,
                typeof(bool));

        // Create the new lambda with updated signature
        _matchExpression = Lambda(
            functionType,
            updatedBoy,
            signalDataArg,
            workflowInstanceArg,
            localVarsArg
            );

        // Replace workflow instance constants with the workflow instance parameter
        Expression OnVisitConstant(ConstantExpression node)
        {
            if (node.Value == _currentWorkflowInstance)
                return workflowInstanceArg;
            return base.VisitConstant(node);
        }

        // Replace old parameters with new ones
        Expression OnVisitParameter(ParameterExpression parameter)
        {
            if (parameter == expression.Parameters[0])
                return signalDataArg;
            return base.VisitParameter(parameter);
        }
    }

    /// <summary>
    /// Detects the closure type from compiler-generated closure constants in the expression tree.
    /// </summary>
    /// <returns>The closure type if found, otherwise <see cref="object"/>.</returns>
    private Type GetClosureType()
    {
        Type result = null;
        var getClosureTypeVisitor = new GenericVisitor();

        getClosureTypeVisitor.OnVisitConstant(OnVisitConstant);
        getClosureTypeVisitor.StopWhen(_ => result != null);
        getClosureTypeVisitor.Visit(_matchExpression);

        Expression OnVisitConstant(ConstantExpression node)
        {
            // Look for compiler-generated closure types
            if (node.Type.Name.StartsWith(CompilerConstants.ClosurePrefix))
                result = node.Type;
            return base.VisitConstant(node);
        }

        return result ?? typeof(object);
    }

    /// <summary>
    /// Identifies exact match parts in the expression.
    /// Exact match parts are equality comparisons where one side uses signalData and the other is a constant value.
    /// Also translates boolean properties to explicit equality comparisons (e.g., signalData.IsHappy => signalData.IsHappy == true).
    /// </summary>
    private void FindExactMatchParts()
    {
        MatchTransformationResult.MatchExpression = _matchExpression;
        var constantTranslationVisitor = new GenericVisitor();
        constantTranslationVisitor.OnVisitBinary(VisitBinary);
        _matchExpression = (LambdaExpression)constantTranslationVisitor.Visit(_matchExpression);

        Expression VisitBinary(BinaryExpression node)
        {
            // Only process boolean expressions
            if (node.Type != typeof(bool))
                return base.VisitBinary(node);

            // Look for equality comparisons: value == signalData property
            if (node.NodeType == ExpressionType.Equal)
            {
                if (CanConvertToSimpleString(node.Left) && IsSignalDataExpression(node.Right, out _))
                    _expressionParts.Add(new(node, node.Right, node.Left));
                else if (CanConvertToSimpleString(node.Right) && IsSignalDataExpression(node.Left, out _))
                    _expressionParts.Add(new(node, node.Left, node.Right));
            }

            // Translate boolean properties to explicit comparisons:
            // signalData.IsHappy => signalData.IsHappy == true
            // !signalData.IsHappy => signalData.IsHappy == false
            if (IsSignalDataBoolean(node, node.Left, out Expression newExpression1))
                return newExpression1;
            if (IsSignalDataBoolean(node, node.Right, out Expression newExpression2))
                return newExpression2;

            return base.VisitBinary(node);
        }

        /// <summary>
        /// Checks if an expression can be converted to a simple string value (constant or computable at compile time).
        /// </summary>
        bool CanConvertToSimpleString(Expression expression)
        {
            // CompilerConstants are always convertible
            if (expression is ConstantExpression constantExpression)
                return true;

            // Can't convert if it uses signalData parameter
            var usesSignalData = IsSignalDataExpression(expression, out int signalDataUseCount) || signalDataUseCount > 0;
            if (usesSignalData) return false;

            // Try to evaluate the expression
            var result = GetExpressionValue(expression);
            if (result != null)
                return result.CanConvertToSimpleString();

            throw new NotSupportedException(
                $"Can't use expression [{expression}] in match because it's evaluated to [NULL].");
        }

        /// <summary>
        /// Evaluates an expression at compile time using the workflow instance.
        /// </summary>
        object GetExpressionValue(Expression expression)
        {
            try
            {
                var functionType = typeof(Func<,>).MakeGenericType(_matchExpression.Parameters[1].Type, typeof(object));
                var getExpValue = Lambda(functionType, Convert(expression, typeof(object)), _matchExpression.Parameters[1]).CompileFast();
                return getExpValue.DynamicInvoke(_currentWorkflowInstance);
            }
            catch (Exception ex)
            {
                throw new NotSupportedException(message:
                    $"Can't use expression [{expression}] in match because we can't compute its value.\n" +
                    $"Try to rewrite it in another form.\n" +
                    $"Exception: {ex}");
            }
        }
    }

    /// <summary>
    /// Translates boolean signalData expressions to explicit equality comparisons.
    /// Handles both direct boolean properties (signalData.IsHappy) and negated ones (!signalData.IsHappy).
    /// </summary>
    private bool IsSignalDataBoolean(BinaryExpression node, Expression operand, out Expression newExpression)
    {
        newExpression = null;

        // Boolean arithmetic operators that should not be translated
        var booleanArthimaticOp = new[]
        {
            ExpressionType.And,
            ExpressionType.Or,
            ExpressionType.ExclusiveOr
        };

        var isLeft = node.Left == operand;
        var otherOperand = isLeft ? node.Right : node.Left;

        // Check if operand is a boolean signalData expression
        var isSignalDataBoolean =
            operand.Type == typeof(bool) &&
            IsSignalDataExpression(operand, out _) &&
            (operand is ParameterExpression || operand is MemberExpression) &&
            otherOperand is not ConstantExpression &&
            !booleanArthimaticOp.Contains(node.NodeType);

        // Translate to: operand == true
        var translatedBoolean = isSignalDataBoolean ? MakeBinary(ExpressionType.Equal, operand, Constant(true)) : null;

        // Handle negated boolean: !signalData.IsHappy
        if (isSignalDataBoolean is false && operand is UnaryExpression unaryExpression)
        {
            isSignalDataBoolean =
                unaryExpression.NodeType == ExpressionType.Not &&
                IsSignalDataExpression(unaryExpression.Operand, out _) &&
                (unaryExpression.Operand is MemberExpression || unaryExpression.Operand is ParameterExpression);

            // Translate to: operand == false
            translatedBoolean = isSignalDataBoolean ? MakeBinary(ExpressionType.Equal, unaryExpression.Operand, Constant(false)) : null;
        }

        // Rebuild the binary expression with the translated boolean
        if (isSignalDataBoolean)
        {
            newExpression =
                isLeft ?
                base.VisitBinary(MakeBinary(node.NodeType, translatedBoolean, otherOperand)) :
                base.VisitBinary(MakeBinary(node.NodeType, otherOperand, translatedBoolean));
        }
        return isSignalDataBoolean;
    }

    /// <summary>
    /// Determines which expression parts are mandatory for the match by testing the expression with each part set to false.
    /// A part is mandatory if the expression evaluates to false when that part is false and all others are true.
    /// </summary>
    private void MarkMandatoryParts()
    {
        Expression expressionToCheck = null;

        var changeToBooleans = new GenericVisitor();
        changeToBooleans.OnVisitBinary(VisitBinary);
        changeToBooleans.OnVisitMethodCall(VisitMethodCall);

        // Test each expression part to see if it's mandatory
        foreach (var expressionPart in _expressionParts)
        {
            if (expressionPart.IsMandatory) continue;

            expressionToCheck = expressionPart.Expression;
            var expression = changeToBooleans.Visit(_matchExpression.Body);
            try
            {
                // Compile and evaluate: if result is false, this part is mandatory
                var compiled = Lambda<Func<bool>>(expression).CompileFast();
                expressionPart.IsMandatory = !compiled();
            }
            catch (Exception)
            {
                // If evaluation fails, assume not mandatory
                expressionPart.IsMandatory = false;
            }
        }

        // Replace the current expression part being tested with false,
        // and all simple logical expressions with true
        Expression VisitBinary(BinaryExpression node)
        {
            if (node == expressionToCheck)
                return Constant(false);
            if (node.Left == expressionToCheck)
                return base.VisitBinary(MakeBinary(node.NodeType, Constant(false), node.Right));
            if (node.Right == expressionToCheck)
                return base.VisitBinary(MakeBinary(node.NodeType, node.Left, Constant(false)));
            else if (IsSimpleLogicalExpression(node))
                return Constant(true);
            return base.VisitBinary(node);
        }

        // Replace boolean method calls with true
        Expression VisitMethodCall(MethodCallExpression methodCallExpression)
        {
            if (methodCallExpression.Method.ReturnType == typeof(bool))
                return Constant(true);
            return base.VisitMethodCall(methodCallExpression);
        }
    }

    /// <summary>
    /// Determines if matching only mandatory parts is sufficient for a full match.
    /// Tests the expression with mandatory parts as true and all others as false.
    /// </summary>
    private void SetIsMandatoryPartFullMatchValue()
    {
        var mandatoryExactParts = _expressionParts.Where(x => x.IsMandatory).ToList();

        if (mandatoryExactParts.Count == 0)
        {
            MatchTransformationResult.IsExactMatchFullMatch = false;
            return;
        }

        var replacer = new GenericVisitor();

        // Replace mandatory parts with true, all others with false
        replacer.OnVisitBinary(node =>
        {
            if (node.Type != typeof(bool))
                return base.VisitBinary(node);

            return mandatoryExactParts.Any(x => x.Expression == node)
                ? Constant(true)
                : Constant(false);
        });

        replacer.OnVisitMethodCall(node =>
        {
            return node.Method.ReturnType == typeof(bool)
                ? Constant(false)
                : base.VisitMethodCall(node);
        });

        // If expression evaluates to true, mandatory parts alone are sufficient
        var transformedBody = replacer.Visit(_matchExpression.Body);
        var compiled = Lambda<Func<bool>>(transformedBody).CompileFast();
        MatchTransformationResult.IsExactMatchFullMatch = compiled();
    }

    /// <summary>
    /// Checks if an expression uses only signalData parameter (no workflow instance or closure variables).
    /// </summary>
    /// <param name="expression">The expression to check.</param>
    /// <param name="signalDataUseCount">Output: the number of times signalData parameter is used.</param>
    /// <returns>True if the expression uses only signalData parameter.</returns>
    private bool IsSignalDataExpression(Expression expression, out int signalDataUseCount)
    {
        var checkUseParamter = new GenericVisitor();
        var signalDataParameterCount = 0;
        var instanceAndClosureCount = 0;

        checkUseParamter.OnVisitParameter(param =>
        {
            // Count signalData parameter usage
            if (param == _matchExpression.Parameters[0])
                signalDataParameterCount++;
            // Count workflow instance and closure parameter usage
            if (param == _matchExpression.Parameters[1] || param == _matchExpression.Parameters[2])
                instanceAndClosureCount++;
            return param;
        });

        checkUseParamter.OnVisitConstant(constant =>
        {
            // Count closure constant usage
            if (constant.Type.Name.StartsWith(CompilerConstants.ClosurePrefix))
                instanceAndClosureCount++;
            return constant;
        });

        checkUseParamter.Visit(expression);
        bool useSignalDataOnly = signalDataParameterCount > 0 && instanceAndClosureCount == 0;
        signalDataUseCount = signalDataParameterCount;
        return useSignalDataOnly;
    }

    /// <summary>
    /// Checks if a binary expression is a simple logical expression (contains exactly one logical operator).
    /// </summary>
    private bool IsSimpleLogicalExpression(BinaryExpression node)
    {
        var _booleanLogicalOps = new ExpressionType[]
           {
                ExpressionType.AndAlso,
                ExpressionType.OrElse,
                ExpressionType.Equal,
                ExpressionType.NotEqual,
                ExpressionType.LessThan,
                ExpressionType.LessThanOrEqual,
                ExpressionType.GreaterThan,
                ExpressionType.GreaterThanOrEqual,
                ExpressionType.And,
                ExpressionType.Or,
                ExpressionType.ExclusiveOr,
           };

        var logicalOpsCount = 0;
        var countLogicalOpsVisitor = new GenericVisitor();
        countLogicalOpsVisitor.OnVisitBinary(binaryNode =>
        {
            if (_booleanLogicalOps.Contains(binaryNode.NodeType) && binaryNode.Type == typeof(bool))
                logicalOpsCount++;
            return binaryNode;
        });

        countLogicalOpsVisitor.Visit(node);
        return logicalOpsCount == 1 && node.Type == typeof(bool);
    }
}
