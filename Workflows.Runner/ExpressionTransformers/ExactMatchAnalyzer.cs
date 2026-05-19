using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class ExactMatchAnalyzer : ExpressionVisitor
    {
        private readonly LambdaExpression _originalExpression;
        private readonly List<string> _signalPaths = new();
        private readonly List<Expression> _instanceValues = new();

        // Tracks nodes we successfully extracted so we can prove a "Full Match" later
        private readonly List<Expression> _extractedNodes = new();

        private bool _isFullMatch = false;

        public ExactMatchAnalyzer(LambdaExpression originalExpression)
        {
            _originalExpression = originalExpression;
        }

        public void Analyze()
        {
            try
            {
                // 1. Traverse the tree. Evaluates every condition independently.
                Visit(_originalExpression.Body);

                // 2. Mathematically evaluate if the extracted nodes cover the ENTIRE query.
                EvaluateFullMatch();
            }
            catch
            {
                _isFullMatch = false; // Graceful fallback
            }
        }

        public List<string> SignalExactMatchPaths => _signalPaths;
        public bool IsExactMatchFullMatch => _isFullMatch;

        public Expression<Func<object, object, string[]>> InstanceExactMatchExpression
        {
            get
            {
                if (!_instanceValues.Any()) return null;

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var stateParam = Expression.Parameter(typeof(object), "state");

                var convertMethod = typeof(Convert).GetMethod("ToString", new[] { typeof(object) });
                var stringValues = _instanceValues.Select(v =>
                    Expression.Call(convertMethod, Expression.Convert(v, typeof(object)))
                );

                var arrayExpr = Expression.NewArrayInit(typeof(string), stringValues);

                return Expression.Lambda<Func<object, object, string[]>>(
                    arrayExpr,
                    instanceParam,
                    stateParam
                );
            }
        }

        // ------------------------------------------------------------------
        // TREE TRAVERSAL: Finding and Testing Candidates
        // ------------------------------------------------------------------

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType == ExpressionType.Equal)
            {
                TryExtract(node, node.Left, node.Right);
                return node; // Stop traversing the children of this == node
            }
            return base.VisitBinary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == "Equals" && node.Arguments.Count == 1 && node.Object != null)
            {
                TryExtract(node, node.Object, node.Arguments[0]);
                return node;
            }
            return base.VisitMethodCall(node);
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Not &&
                node.Operand is MemberExpression me &&
                me.Type == typeof(bool) &&
                IsSignalParameter(me))
            {
                if (IsMandatory(node))
                {
                    _signalPaths.Add(GetPath(me));
                    _instanceValues.Add(Expression.Constant(false));
                    _extractedNodes.Add(node);
                }
                return node;
            }
            return base.VisitUnary(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Type == typeof(bool) && IsSignalParameter(node))
            {
                if (IsMandatory(node))
                {
                    _signalPaths.Add(GetPath(node));
                    _instanceValues.Add(Expression.Constant(true));
                    _extractedNodes.Add(node);
                }
                return node;
            }
            return base.VisitMember(node);
        }

        // ------------------------------------------------------------------
        // EVALUATION LOGIC
        // ------------------------------------------------------------------

        private void TryExtract(Expression originalNode, Expression left, Expression right)
        {
            bool leftIsSignal = IsSignalParameter(left);
            bool rightIsSignal = IsSignalParameter(right);

            string path = null;
            Expression valueNode = null;

            if (leftIsSignal && !rightIsSignal)
            {
                path = GetPath(left);
                valueNode = right;
            }
            else if (rightIsSignal && !leftIsSignal)
            {
                path = GetPath(right);
                valueNode = left;
            }

            // ONLY extract it if the Mutator/Folder mathematically proves it's mandatory
            if (path != null && IsMandatory(originalNode))
            {
                _signalPaths.Add(path);
                _instanceValues.Add(valueNode);
                _extractedNodes.Add(originalNode);
            }
        }

        private bool IsMandatory(Expression targetNode)
        {
            // Swap this specific condition to 'false'
            var mutator = new TargetMutator(targetNode, false);
            var mutatedTree = mutator.Visit(_originalExpression.Body);

            // Fold the tree and check if it collapsed entirely to 'false'
            var folder = new BooleanFolder();
            var foldedTree = folder.Visit(mutatedTree);

            return folder.IsConstantFalse(foldedTree);
        }

        private void EvaluateFullMatch()
        {
            if (!_extractedNodes.Any())
            {
                _isFullMatch = false;
                return;
            }

            var tree = _originalExpression.Body;

            // To prove a full match, set ALL extracted nodes to 'true'
            foreach (var node in _extractedNodes)
            {
                var mutator = new TargetMutator(node, true);
                tree = mutator.Visit(tree);
            }

            // If the tree collapses entirely to 'true', there is no leftover logic!
            var folder = new BooleanFolder();
            var folded = folder.Visit(tree);

            _isFullMatch = folder.IsConstantTrue(folded);
        }

        // ------------------------------------------------------------------
        // UTILITIES & UNWRAPPING
        // ------------------------------------------------------------------

        private bool IsSignalParameter(Expression node)
        {
            node = UnwrapConvert(node);
            while (node is MemberExpression me) node = UnwrapConvert(me.Expression);
            return node is ParameterExpression pe && pe == _originalExpression.Parameters[0];
        }

        private string GetPath(Expression node)
        {
            var parts = new List<string>();
            node = UnwrapConvert(node);

            while (node is MemberExpression me)
            {
                parts.Add(me.Member.Name);
                node = UnwrapConvert(me.Expression);
            }
            parts.Reverse();
            return string.Join(".", parts);
        }

        private Expression UnwrapConvert(Expression node)
        {
            while (node != null && node.NodeType == ExpressionType.Convert && node is UnaryExpression ue)
            {
                node = ue.Operand;
            }
            return node;
        }

        // ------------------------------------------------------------------
        // NESTED CLASSES: The Engine
        // ------------------------------------------------------------------

        private class TargetMutator : ExpressionVisitor
        {
            private readonly Expression _target;
            private readonly bool _replaceWithValue;

            public TargetMutator(Expression target, bool replaceWithValue)
            {
                _target = target;
                _replaceWithValue = replaceWithValue;
            }

            public override Expression Visit(Expression node)
            {
                // Relying on Reference Equality to swap the exact node instance
                if (node == _target) return Expression.Constant(_replaceWithValue);
                return base.Visit(node);
            }
        }

        private class BooleanFolder : ExpressionVisitor
        {
            protected override Expression VisitBinary(BinaryExpression node)
            {
                var left = Visit(node.Left);
                var right = Visit(node.Right);

                if (node.NodeType == ExpressionType.AndAlso)
                {
                    if (IsConstantFalse(left) || IsConstantFalse(right)) return Expression.Constant(false);
                    if (IsConstantTrue(left)) return right;
                    if (IsConstantTrue(right)) return left;
                }
                else if (node.NodeType == ExpressionType.OrElse)
                {
                    if (IsConstantTrue(left) || IsConstantTrue(right)) return Expression.Constant(true);
                    if (IsConstantFalse(left)) return right;
                    if (IsConstantFalse(right)) return left;
                }

                if (left != node.Left || right != node.Right)
                    return node.Update(left, node.Conversion, right);

                return node;
            }

            protected override Expression VisitConditional(ConditionalExpression node)
            {
                var test = Visit(node.Test);

                if (IsConstantTrue(test)) return Visit(node.IfTrue);
                if (IsConstantFalse(test)) return Visit(node.IfFalse);

                var ifTrue = Visit(node.IfTrue);
                var ifFalse = Visit(node.IfFalse);

                if (test != node.Test || ifTrue != node.IfTrue || ifFalse != node.IfFalse)
                    return node.Update(test, ifTrue, ifFalse);

                return node;
            }

            protected override Expression VisitUnary(UnaryExpression node)
            {
                var operand = Visit(node.Operand);

                if (node.NodeType == ExpressionType.Not)
                {
                    if (IsConstantTrue(operand)) return Expression.Constant(false);
                    if (IsConstantFalse(operand)) return Expression.Constant(true);
                }

                if (operand != node.Operand)
                    return node.Update(operand);

                return node;
            }

            public bool IsConstantFalse(Expression ex) => ex is ConstantExpression c && c.Value is bool b && !b;
            public bool IsConstantTrue(Expression ex) => ex is ConstantExpression c && c.Value is bool b && b;
        }
    }
}