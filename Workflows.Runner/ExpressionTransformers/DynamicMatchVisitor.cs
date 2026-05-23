using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class DynamicMatchVisitor : ExpressionVisitor
    {
        private readonly LambdaExpression _originalLambda;
        private bool _isUnsupportedNodeFound;
        private readonly HashSet<Expression> _partialNodes = new();
        private static readonly ConstantExpression UnknownNode = Expression.Constant("__UNKNOWN__");

        public LambdaExpression TypedResult { get; private set; }
        public Expression<Func<JsonElement, JsonElement, JsonElement, bool>> Result { get; private set; }
        public bool IsFullMatch => !_isUnsupportedNodeFound && Result != null;

        public DynamicMatchVisitor(LambdaExpression originalLambda)
        {
            _originalLambda = originalLambda ?? throw new ArgumentNullException(nameof(originalLambda));
        }

        private void MarkPartial(Expression node) { if (node != null) _partialNodes.Add(node); }
        private bool IsPartial(Expression node) => node != null && _partialNodes.Contains(node);

        public void Build()
        {
            // PASS 1: Walk the original typed tree and safely prune unsupported logic
            var safeTypedBody = Visit(_originalLambda.Body);

            // CRITICAL: Return null if the tree couldn't be saved or collapsed safely
            if (safeTypedBody == UnknownNode || safeTypedBody == null || _isUnsupportedNodeFound)
            {
                Result = null;
                TypedResult = null;
                return;
            }

            TypedResult = Expression.Lambda(safeTypedBody, _originalLambda.Parameters);

            // PASS 2: Translate the perfectly safe POCO tree into a JsonElement execution tree
            var jsonTranslator = new JsonTranslationVisitor(_originalLambda.Parameters);
            var jsonBody = jsonTranslator.Visit(safeTypedBody);

            Result = Expression.Lambda<Func<JsonElement, JsonElement, JsonElement, bool>>(
                jsonBody, jsonTranslator.JsonSignal, jsonTranslator.JsonState, jsonTranslator.JsonInstance);
        }

        // --- Pass 1: Pruning Logic (Operates on strongly-typed POCO tree) ---

        protected override Expression VisitBinary(BinaryExpression node)
        {
            var left = Visit(node.Left);
            var right = Visit(node.Right);

            if (left == UnknownNode && right == UnknownNode) return UnknownNode;

            if (left == UnknownNode || right == UnknownNode)
            {
                if (node.NodeType == ExpressionType.AndAlso)
                {
                    var kept = left == UnknownNode ? right : left;
                    MarkPartial(kept);
                    return kept;
                }
                _isUnsupportedNodeFound = true;
                return UnknownNode;
            }

            var updated = node.Update(left, node.Conversion, right);
            if (IsPartial(left) || IsPartial(right)) MarkPartial(updated);
            return updated;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            var operand = Visit(node.Operand);
            if (operand == UnknownNode) return UnknownNode;

            // Negation on a tainted partial node risks False Negatives. Abort.
            if (node.NodeType == ExpressionType.Not && IsPartial(operand))
            {
                _isUnsupportedNodeFound = true;
                return UnknownNode;
            }

            var updated = node.Update(operand);
            if (IsPartial(operand)) MarkPartial(updated);
            return updated;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Only .Equals() is native. Everything else (like DbCheck()) triggers Unknown.
            if (node.Method.Name == "Equals" && node.Arguments.Count == 1 && node.Object != null)
            {
                var obj = Visit(node.Object);
                var arg = Visit(node.Arguments[0]);

                if (obj == UnknownNode || arg == UnknownNode) return UnknownNode;

                var updated = node.Update(obj, new[] { arg });
                if (IsPartial(obj) || IsPartial(arg)) MarkPartial(updated);
                return updated;
            }

            _isUnsupportedNodeFound = true;
            return UnknownNode;
        }

        // --- Pass 2: The Internal JSON Mapper ---
        private class JsonTranslationVisitor : ExpressionVisitor
        {
            public ParameterExpression JsonSignal { get; } = Expression.Parameter(typeof(JsonElement), "signalData");
            public ParameterExpression JsonState { get; } = Expression.Parameter(typeof(JsonElement), "stateData");
            public ParameterExpression JsonInstance { get; } = Expression.Parameter(typeof(JsonElement), "workflowInstance");

            private readonly IReadOnlyList<ParameterExpression> _originalParams;

            public JsonTranslationVisitor(IReadOnlyList<ParameterExpression> originalParams)
            {
                _originalParams = originalParams;
            }

            protected override Expression VisitMember(MemberExpression node)
            {
                var root = GetRoot(node);
                if (root is ParameterExpression p)
                {
                    ParameterExpression target = null;
                    if (_originalParams.Count > 0 && p == _originalParams[0]) target = JsonSignal;
                    else if (_originalParams.Count > 1 && p == _originalParams[1]) target = JsonState;
                    else if (_originalParams.Count > 2 && p == _originalParams[2]) target = JsonInstance;

                    if (target != null)
                    {
                        var path = GetPath(node);
                        var method = typeof(JsonElementExtensions).GetMethods()
                            .First(x => x.Name == "Get" && x.IsGenericMethod)
                            .MakeGenericMethod(node.Type);

                        return Expression.Call(method, target, Expression.Constant(path));
                    }
                }
                return base.VisitMember(node);
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                // JsonElement doesn't have .Equals(), so map it mathematically to ==
                if (node.Method.Name == "Equals" && node.Arguments.Count == 1 && node.Object != null)
                {
                    var left = Visit(node.Object);
                    var right = Visit(node.Arguments[0]);
                    return Expression.Equal(left, right);
                }
                return base.VisitMethodCall(node);
            }

            private static Expression GetRoot(Expression node)
            {
                while (node is MemberExpression me) node = me.Expression;
                return node;
            }

            private static string GetPath(MemberExpression node)
            {
                var path = new List<string>();
                while (node != null)
                {
                    path.Add(node.Member.Name);
                    node = node.Expression as MemberExpression;
                }
                path.Reverse();
                return string.Join(".", path);
            }
        }
    }
}