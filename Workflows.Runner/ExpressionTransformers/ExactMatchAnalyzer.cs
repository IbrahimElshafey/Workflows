using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Workflows.Runner.ExpressionTransformers
{
    // ----------------------------------------------------------------------
    // 2. The Tier 1 Analyzer (Extracts SQL Paths)
    // ----------------------------------------------------------------------

    internal class ExactMatchAnalyzer : ExpressionVisitor
    {
        private readonly LambdaExpression _originalExpression;
        private readonly List<string> _signalPaths = new();
        private readonly List<Expression> _instanceValues = new();
        private bool _isFullMatch = true;

        public ExactMatchAnalyzer(LambdaExpression originalExpression)
        {
            _originalExpression = originalExpression;
        }

        public void Analyze()
        {
            Visit(_originalExpression.Body);
        }

        public List<string> SignalExactMatchPaths => _signalPaths;
        public bool IsExactMatchFullMatch => _isFullMatch;

        public LambdaExpression InstanceExactMatchExpression
        {
            get
            {
                if (!_instanceValues.Any()) return null;

                var arrayExpr = Expression.NewArrayInit(
                    typeof(object),
                    _instanceValues.Select(v => Expression.Convert(v, typeof(object)))
                );

                return Expression.Lambda(arrayExpr, _originalExpression.Parameters);
            }
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType == ExpressionType.AndAlso || node.NodeType == ExpressionType.Equal)
            {
                if (node.NodeType == ExpressionType.Equal)
                {
                    ExtractEquality(node.Left, node.Right);
                }
                return base.VisitBinary(node);
            }

            _isFullMatch = false;
            return base.VisitBinary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Support for: s.Id.Equals(this.Id)
            if (node.Method.Name == "Equals" && node.Arguments.Count == 1 && node.Object != null)
            {
                ExtractEquality(node.Object, node.Arguments[0]);
                return base.VisitMethodCall(node);
            }

            // Any other method call makes it NOT a full exact match
            _isFullMatch = false;
            return base.VisitMethodCall(node);
        }

        private void ExtractEquality(Expression left, Expression right)
        {
            bool leftIsSignal = IsSignalParameter(left);
            bool rightIsSignal = IsSignalParameter(right);

            if (leftIsSignal && !rightIsSignal)
            {
                _signalPaths.Add(GetPath(left));
                _instanceValues.Add(right);
            }
            else if (rightIsSignal && !leftIsSignal)
            {
                _signalPaths.Add(GetPath(right));
                _instanceValues.Add(left);
            }
            else
            {
                _isFullMatch = false;
            }
        }

        // Issue 3: handle standalone boolean signal members: s.IsActive (→ s.IsActive == true)
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Type == typeof(bool) && IsSignalParameter(node))
            {
                _signalPaths.Add(GetPath(node));
                _instanceValues.Add(Expression.Constant(true));
            }
            return base.VisitMember(node);
        }

        // Issue 3: handle negated boolean signal members: !s.IsActive (→ s.IsActive == false)
        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Not &&
                node.Operand is MemberExpression me &&
                me.Type == typeof(bool) &&
                IsSignalParameter(me))
            {
                _signalPaths.Add(GetPath(me));
                _instanceValues.Add(Expression.Constant(false));
                return node;
            }
            return base.VisitUnary(node);
        }

        private bool IsSignalParameter(Expression node)
        {
            while (node is MemberExpression me) node = me.Expression;
            return node is ParameterExpression pe && pe == _originalExpression.Parameters[0];
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
    }
}