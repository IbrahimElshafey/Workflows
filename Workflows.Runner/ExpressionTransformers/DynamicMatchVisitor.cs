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
        private readonly LambdaExpression _originalLambda;
        private readonly ParameterExpression _signalDataParam;
        private readonly ParameterExpression _workflowInstanceParam;
        private readonly ParameterExpression _stateDataParam;
        private bool _isUnsupportedNodeFound;

        // Signature is now exactly aligned with the 3 inputs: (Signal, Instance, State/Closure)
        public Expression<Func<JsonElement, JsonElement, JsonElement, bool>> Result { get; private set; }
        public bool IsFullMatch => !_isUnsupportedNodeFound;

        public DynamicMatchVisitor(LambdaExpression matchExpression)
        {
            _originalLambda = matchExpression;

            // The JSON elements that the Orchestrator will pass in at runtime
            _signalDataParam = Expression.Parameter(typeof(JsonElement), "signalData");
            _workflowInstanceParam = Expression.Parameter(typeof(JsonElement), "workflowInstance");
            _stateDataParam = Expression.Parameter(typeof(JsonElement), "stateData");

            var visitedBody = Visit(matchExpression.Body);

            if (!_isUnsupportedNodeFound && visitedBody != null)
            {
                Result = Expression.Lambda<Func<JsonElement, JsonElement, JsonElement, bool>>(
                    visitedBody,
                    _signalDataParam,
                    _workflowInstanceParam,
                    _stateDataParam);
            }
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            var originalParam = GetRootParameter(node);

            if (originalParam == null || !CanConvertToString(node.Type))
            {
                _isUnsupportedNodeFound = true;
                return base.VisitMember(node);
            }

            // Map the original C# parameter to our new JsonElement parameters
            var targetJsonParam = GetMappedParameter(originalParam);
            if (targetJsonParam == null)
            {
                _isUnsupportedNodeFound = true;
                return base.VisitMember(node);
            }

            var path = GetPath(node);

            // Translates `state.OrderId` -> `JsonElementExtensions.Get<int>(stateData, "OrderId")`
            var getValueMethod = typeof(JsonElementExtensions)
                .GetMethods()
                .First(x => x.Name == "Get" && x.IsGenericMethod)
                .MakeGenericMethod(node.Type);

            return Expression.Call(getValueMethod, targetJsonParam, Expression.Constant(path));
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Safely translate .Equals() into == so Tier 1.5 can still evaluate it in RAM
            if (node.Method.Name == "Equals" && node.Arguments.Count == 1 && node.Object != null)
            {
                var left = Visit(node.Object);
                var right = Visit(node.Arguments[0]);

                if (left != null && right != null)
                {
                    return Expression.Equal(left, right);
                }
            }

            _isUnsupportedNodeFound = true;
            return base.VisitMethodCall(node);
        }

        private ParameterExpression GetRootParameter(Expression node)
        {
            while (node is MemberExpression me) node = me.Expression;
            return node as ParameterExpression;
        }

        /// <summary>
        /// Safely maps the original C# parameter to the JsonElement parameter based on its position,
        /// avoiding brittle string-name matching.
        /// </summary>
        private ParameterExpression GetMappedParameter(ParameterExpression originalParam)
        {
            // 0 = Incoming Signal Data
            if (originalParam == _originalLambda.Parameters[0]) return _signalDataParam;

            // 1 = Workflow Container Instance (Domain State)
            if (_originalLambda.Parameters.Count > 1 && originalParam == _originalLambda.Parameters[1]) return _workflowInstanceParam;

            // 2 = Explicit State (.WithState) or Captured Closure
            if (_originalLambda.Parameters.Count > 2 && originalParam == _originalLambda.Parameters[2]) return _stateDataParam;

            return null;
        }

        private string GetPath(Expression node)
        {
            var parts = new List<string>();
            while (node is MemberExpression me)
            {
                parts.Add(me.Member.Name);
                node = me.Expression;
            }
            parts.Reverse();
            return string.Join(".", parts);
        }

        private bool CanConvertToString(Type type) =>
            type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) || type == typeof(Guid) || type.IsEnum;
    }
}