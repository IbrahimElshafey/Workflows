using System.Linq.Expressions;

using System;
using System.Collections.Generic;
namespace Workflows.Handler.Expressions
{
    public class GenericVisitor : ExpressionVisitor
    {
        private readonly List<VisitNodeWorkflow> _visitors = new();
        private Func<Expression, bool> _stopCondition;

        public void AddVisitor(
            Func<Expression, bool> whenExpressionMatch,
            Func<Expression, Expression> visitWorkflow)
        {
            _visitors.Add(new VisitNodeWorkflow(whenExpressionMatch, visitWorkflow));
        }

        public void ClearVisitors() => _visitors.Clear();

        public override Expression Visit(Expression node)
        {
            if (_stopCondition?.Invoke(node) is true) return node;
            foreach (var visitor in _visitors)
            {
                if (visitor.WhenExpressionMatch(node))
                    return base.Visit(visitor.VisitWorkflow(node));
            }
            return base.Visit(node);
        }

        internal void OnVisitBinary(Func<BinaryExpression, Expression> binaryVisitFunc)
        {
            _visitors.Add(
               new VisitNodeWorkflow(
                  ex => ex is BinaryExpression,
                   ex => binaryVisitFunc((BinaryExpression)ex)));
        }

        internal void OnVisitParameter(Func<ParameterExpression, Expression> parameterVisitFunc)
        {
            _visitors.Add(
                new VisitNodeWorkflow(
                    ex => ex is ParameterExpression,
                    ex => parameterVisitFunc((ParameterExpression)ex)));
        }
        internal void OnVisitConstant(Func<ConstantExpression, Expression> constantVisitFunc)
        {
            _visitors.Add(
                new VisitNodeWorkflow(
                    ex => ex is ConstantExpression,
                    ex => constantVisitFunc((ConstantExpression)ex)));
        }

        internal void OnVisitUnary(Func<UnaryExpression, Expression> visitUnary)
        {
            _visitors.Add(
              new VisitNodeWorkflow(
                 ex => ex is UnaryExpression,
                  ex => visitUnary((UnaryExpression)ex)));
        }

        internal void OnVisitMember(Func<MemberExpression, Expression> visitMember)
        {
            _visitors.Add(
              new VisitNodeWorkflow(
                 ex => ex is MemberExpression,
                  ex => visitMember((MemberExpression)ex)));
        }

        internal void OnVisitMethodCall(Func<MethodCallExpression, Expression> visitCall)
        {
            _visitors.Add(
              new VisitNodeWorkflow(
                 ex => ex is MethodCallExpression,
                  ex => visitCall((MethodCallExpression)ex)));
        }

        internal void StopWhen(Func<Expression, bool> stopCondition)
        {
            _stopCondition = stopCondition;
        }

        private class VisitNodeWorkflow
        {
            public VisitNodeWorkflow(Func<Expression, bool> whenExpressionMatch, Func<Expression, Expression> visitWorkflow)
            {
                WhenExpressionMatch = whenExpressionMatch;
                VisitWorkflow = visitWorkflow;
            }
            public Func<Expression, bool> WhenExpressionMatch { get; }
            public Func<Expression, Expression> VisitWorkflow { get; }
        }
    }
}
