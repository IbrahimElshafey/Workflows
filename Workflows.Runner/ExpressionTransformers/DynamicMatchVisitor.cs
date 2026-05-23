using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;

namespace Workflows.Runner.ExpressionTransformers
{
    // ----------------------------------------------------------------------
    // 3. The Tier 1.5 JsonElement Visitor
    // ----------------------------------------------------------------------

    /// <summary>
    /// Translates match expression expressions into JsonElement lookups
    /// so the Orchestrator can evaluate complex POCO logic in RAM without spinning up the Runner.
    /// </summary>
    internal class DynamicMatchVisitor : ExpressionVisitor
    {
        private readonly LambdaExpression _normalizedLambda;
        private readonly ParameterExpression _jsonSignalParam;
        private readonly ParameterExpression _jsonInstanceParam;
        private readonly ParameterExpression _jsonStateParam;

        private bool _isUnsupportedNodeFound;

        private static readonly ConstantExpression UnknownNode = Expression.Constant("__UNKNOWN__");

        // 1. Taint Tracking: Keeps track of nodes that have been partially evaluated
        private readonly HashSet<Expression> _partialNodes = new HashSet<Expression>();

        public Expression<Func<JsonElement, JsonElement, JsonElement, bool>> Result { get; private set; }
        public bool IsFullMatch => !_isUnsupportedNodeFound;

        public DynamicMatchVisitor(LambdaExpression normalizedLambda)
        {
            _normalizedLambda = normalizedLambda;
            _jsonSignalParam = Expression.Parameter(typeof(JsonElement), "signalData");
            _jsonStateParam = Expression.Parameter(typeof(JsonElement), "stateData");
            _jsonInstanceParam = Expression.Parameter(typeof(JsonElement), "workflowInstance");
        }

        private void MarkPartial(Expression node)
        {
            if (node != null) _partialNodes.Add(node);
        }

        private bool IsPartial(Expression node) => node != null && _partialNodes.Contains(node);

        public void Build()
        {
            var visitedBody = Visit(_normalizedLambda.Body);

            if (visitedBody == UnknownNode || visitedBody == null)
            {
                visitedBody = Expression.Constant(true); // Let runner handle it
            }

            Result = Expression.Lambda<Func<JsonElement, JsonElement, JsonElement, bool>>(
                visitedBody,
                _jsonSignalParam,
                _jsonStateParam,
                _jsonInstanceParam);
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            var left = Visit(node.Left);
            var right = Visit(node.Right);

            bool leftIsUnknown = left == UnknownNode;
            bool rightIsUnknown = right == UnknownNode;

            if (leftIsUnknown && rightIsUnknown) return UnknownNode;

            if (leftIsUnknown || rightIsUnknown)
            {
                if (node.NodeType == ExpressionType.AndAlso)
                {
                    // 2. Partial Evaluation for &&
                    var kept = leftIsUnknown ? right : left;

                    // We dropped a condition, meaning this is an OVER-approximation.
                    // We MUST flag this so parent nodes know it's not exact.
                    MarkPartial(kept);
                    return kept;
                }

                // If it's OR (||), ==, !=, >, etc., an unknown side breaks the whole comparison
                return UnknownNode;
            }

            var updatedNode = node.Update(left, node.Conversion, right);

            // 3. Bubble up the Taint: If a child is partial, the parent is partial
            if (IsPartial(left) || IsPartial(right)) MarkPartial(updatedNode);

            return updatedNode;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            var operand = Visit(node.Operand);
            if (operand == UnknownNode) return UnknownNode;

            // 4. THE CRITICAL FIX: The Negation Trap
            // Negating a partial over-approximation creates a dangerous under-approximation (False Negative).
            if (node.NodeType == ExpressionType.Not && IsPartial(operand))
            {
                return UnknownNode;
            }

            var updatedNode = node.Update(operand);
            if (IsPartial(operand)) MarkPartial(updatedNode);

            return updatedNode;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            var rootParam = GetRootParameter(node);

            if (rootParam == null || !IsJsonSupportedType(node.Type))
            {
                _isUnsupportedNodeFound = true;
                return UnknownNode;
            }

            var targetJsonParam = GetMappedParameter(rootParam);
            if (targetJsonParam == null)
            {
                _isUnsupportedNodeFound = true;
                return UnknownNode;
            }

            var path = GetPath(node);
            var getValueMethod = typeof(JsonElementExtensions)
                .GetMethods()
                .First(x => x.Name == "Get" && x.IsGenericMethod)
                .MakeGenericMethod(node.Type);

            return Expression.Call(getValueMethod, targetJsonParam, Expression.Constant(path));
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == "Equals" && node.Arguments.Count == 1 && node.Object != null)
            {
                var left = Visit(node.Object);
                var right = Visit(node.Arguments[0]);

                if (left == UnknownNode || right == UnknownNode)
                {
                    _isUnsupportedNodeFound = true;
                    return UnknownNode;
                }

                if (left != null && right != null)
                {
                    var eqNode = Expression.Equal(left, right);
                    if (IsPartial(left) || IsPartial(right)) MarkPartial(eqNode);
                    return eqNode;
                }
            }

            _isUnsupportedNodeFound = true;
            return UnknownNode;
        }

        private ParameterExpression GetRootParameter(Expression node)
        {
            while (true)
            {
                if (node is MemberExpression me)
                    node = me.Expression;
                // FIX: Step through the UnaryExpression (Expression.Convert) injected by the normalizer!
                else if (node is UnaryExpression ue && ue.NodeType == ExpressionType.Convert)
                    node = ue.Operand;
                else
                    break;
            }
            return node as ParameterExpression;
        }

        private ParameterExpression GetMappedParameter(ParameterExpression normalizedParam)
        {
            // The normalizer ALWAYS outputs parameters in this exact order:
            // [0] = signalData, [1] = state, [2] = instance
            if (normalizedParam == _normalizedLambda.Parameters[0]) return _jsonSignalParam;
            if (normalizedParam == _normalizedLambda.Parameters[1]) return _jsonStateParam;
            if (normalizedParam == _normalizedLambda.Parameters[2]) return _jsonInstanceParam;

            return null;
        }

        private string GetPath(Expression node)
        {
            var parts = new List<string>();
            while (node is MemberExpression me)
            {
                parts.Add(me.Member.Name);
                // Step through casts in the path just in case
                node = me.Expression is UnaryExpression ue && ue.NodeType == ExpressionType.Convert
                    ? ue.Operand
                    : me.Expression;
            }
            parts.Reverse();
            return string.Join(".", parts);
        }

        private bool IsJsonSupportedType(Type type) =>
            type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) || type == typeof(Guid) || type.IsEnum;
    }
}