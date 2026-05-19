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

        public Expression<Func<JsonElement, JsonElement, JsonElement, bool>> Result { get; private set; }
        public bool IsFullMatch => !_isUnsupportedNodeFound;

        // Notice we now expect the NORMALIZED lambda: Func<object, object, object, bool>
        public DynamicMatchVisitor(LambdaExpression normalizedLambda)
        {
            _normalizedLambda = normalizedLambda;
            _jsonSignalParam = Expression.Parameter(typeof(JsonElement), "signalData");
            _jsonStateParam = Expression.Parameter(typeof(JsonElement), "stateData");
            _jsonInstanceParam = Expression.Parameter(typeof(JsonElement), "workflowInstance");
        }

        public void Build()
        {
            var visitedBody = Visit(_normalizedLambda.Body);

            if (!_isUnsupportedNodeFound && visitedBody != null)
            {
                // We align with the orchestrator's JsonElement parameter layout
                Result = Expression.Lambda<Func<JsonElement, JsonElement, JsonElement, bool>>(
                    visitedBody,
                    _jsonSignalParam,
                    _jsonStateParam,
                    _jsonInstanceParam);
            }
            else
            {
                // Explicitly set Result to null if we failed to map the whole expression
                Result = null;
            }
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            var rootParam = GetRootParameter(node);

            if (rootParam == null || !IsJsonSupportedType(node.Type))
            {
                _isUnsupportedNodeFound = true;
                return base.VisitMember(node);
            }

            var targetJsonParam = GetMappedParameter(rootParam);
            if (targetJsonParam == null)
            {
                _isUnsupportedNodeFound = true;
                return base.VisitMember(node);
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

                if (left != null && right != null)
                    return Expression.Equal(left, right);
            }

            _isUnsupportedNodeFound = true;
            return base.VisitMethodCall(node);
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