using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Workflows.Definition;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class MatchExpressionNormalizer
    {
        public (Expression<Func<object, object, object, bool>> NormalizedExpression, LambdaExpression NormalizedExpressionTyped) Normalize(
            LambdaExpression matchExpression,
            WorkflowContainer currentInstance)
        {
            if (matchExpression == null) throw new ArgumentNullException(nameof(matchExpression));
            if (currentInstance == null) throw new ArgumentNullException(nameof(currentInstance));

            // 1. Determine Concrete Types
            Type sigType = matchExpression.Parameters.Count > 0 ? matchExpression.Parameters[0].Type : typeof(object);
            Type stateType = matchExpression.Parameters.Count > 1 ? matchExpression.Parameters[1].Type : typeof(object);
            Type instType = currentInstance.GetType();

            // 2. Create Typed Parameters (Used as both Lambda arguments AND Block local variables)
            var typedSig = Expression.Parameter(sigType, "sig");
            var typedState = Expression.Parameter(stateType, "st");
            var typedInst = Expression.Parameter(instType, "inst");

            // 3. Map old lambda parameters to the new typed ones
            var paramMap = new Dictionary<ParameterExpression, Expression>();
            if (matchExpression.Parameters.Count > 0) paramMap[matchExpression.Parameters[0]] = typedSig;
            if (matchExpression.Parameters.Count > 1) paramMap[matchExpression.Parameters[1]] = typedState;

            // 4. Transform the Body (Swap parameters and replace hardcoded Workflow instance)
            var typedBody = new ParameterMappingVisitor(paramMap).Visit(matchExpression.Body);
            typedBody = new WorkflowInstanceReplacer(typedInst).Visit(typedBody);

            // --- RESULT A: The Pure Typed Lambda ---
            var typedLambda = Expression.Lambda(typedBody, typedSig, typedState, typedInst);

            // --- RESULT B: The Object-Based Block Lambda ---
            var objSig = Expression.Parameter(typeof(object), "signalData");
            var objState = Expression.Parameter(typeof(object), "state");
            var objInst = Expression.Parameter(typeof(object), "instance");

            var blockScope = Expression.Block(
                variables: new[] { typedSig, typedState, typedInst },
                Expression.Assign(typedSig, Expression.Convert(objSig, sigType)),
                Expression.Assign(typedState, Expression.Convert(objState, stateType)),
                Expression.Assign(typedInst, Expression.Convert(objInst, instType)),
                typedBody // Re-use the clean body we already built!
            );

            var objectLambda = Expression.Lambda<Func<object, object, object, bool>>(blockScope, objSig, objState, objInst);

            return (objectLambda, typedLambda);
        }

        private sealed class ParameterMappingVisitor : ExpressionVisitor
        {
            private readonly Dictionary<ParameterExpression, Expression> _map;
            public ParameterMappingVisitor(Dictionary<ParameterExpression, Expression> map) => _map = map;

            protected override Expression VisitParameter(ParameterExpression node) =>
                _map.TryGetValue(node, out var replacement) ? replacement : base.VisitParameter(node);
        }

        private sealed class WorkflowInstanceReplacer : ExpressionVisitor
        {
            private readonly ParameterExpression _typedInst;
            public WorkflowInstanceReplacer(ParameterExpression typedInst) => _typedInst = typedInst;

            protected override Expression VisitConstant(ConstantExpression node)
            {
                if (node.Value is WorkflowContainer) return _typedInst;
                return base.VisitConstant(node);
            }

            protected override Expression VisitMember(MemberExpression node)
            {
                // Intercept properties off a raw workflow instance directly
                if (node.Expression is ConstantExpression c && c.Value is WorkflowContainer)
                    return node.Update(_typedInst);

                var expr = Visit(node.Expression);

                // Constant folding for local closures (<>c__DisplayClass)
                if (expr is ConstantExpression container && container.Value != null)
                {
                    if (node.Member is FieldInfo f) return Expression.Constant(f.GetValue(container.Value), node.Type);
                    if (node.Member is PropertyInfo p) return Expression.Constant(p.GetValue(container.Value), node.Type);
                }

                return node.Update(expr);
            }
        }
    }
}