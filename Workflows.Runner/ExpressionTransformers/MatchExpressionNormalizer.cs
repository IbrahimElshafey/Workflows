using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Workflows.Definition;

namespace Workflows.Runner.ExpressionTransformers
{

    /// <summary>
    /// Normalizes arbitrary workflow match lambda expressions into a unified, 
    /// highly-performant delegate signature: Func<object, object, object, bool>.
    /// </summary>
    internal class MatchExpressionNormalizer
    {
        /// <summary>
        /// Transforms a 1- or 2-parameter lambda expression into the unified execution signature.
        /// </summary>
        public Expression<Func<object, object, object, bool>> Normalize(LambdaExpression matchExpression, WorkflowContainer currentInstance)
        {
            if (matchExpression == null) throw new ArgumentNullException(nameof(matchExpression));
            if (currentInstance == null) throw new ArgumentNullException(nameof(currentInstance));

            // Unified parameters that the engine will pass at runtime
            var signalArg = Expression.Parameter(typeof(object), "signalData");
            var stateArg = Expression.Parameter(typeof(object), "state");
            var instanceArg = Expression.Parameter(typeof(object), "instance");

            var parameterMap = new Dictionary<ParameterExpression, Expression>();

            // Map Signal (Parameter 0)
            if (matchExpression.Parameters.Count >= 1)
            {
                var originalSignal = matchExpression.Parameters[0];
                parameterMap[originalSignal] = Expression.Convert(signalArg, originalSignal.Type);
            }

            // Map State (Parameter 1)
            if (matchExpression.Parameters.Count >= 2)
            {
                var originalState = matchExpression.Parameters[1];
                parameterMap[originalState] = Expression.Convert(stateArg, originalState.Type);
            }
            var instanceType = currentInstance.GetType();
            // Convert the object instance back to the exact workflow class type
            var instanceReplacement = Expression.Convert(instanceArg, instanceType);

            // Execute the structural rewrite
            var visitor = new NormalizationVisitor(parameterMap, currentInstance, instanceReplacement);
            var normalizedBody = visitor.Visit(matchExpression.Body);

            // Return the new unified lambda
            return Expression.Lambda<Func<object, object, object, bool>>(
                normalizedBody,
                signalArg,
                stateArg,
                instanceArg);
        }

        /// <summary>
        /// Stateless visitor scoped to a single normalization pass.
        /// Handles parameter redirection, 'this' mapping, and closure enforcement.
        /// </summary>
        private class NormalizationVisitor : ExpressionVisitor
        {
            private readonly Dictionary<ParameterExpression, Expression> _parameterMap;
            private readonly object _instance;
            private readonly Expression _instanceReplacement;

            public NormalizationVisitor(
                Dictionary<ParameterExpression, Expression> parameterMap,
                object instance,
                Expression instanceReplacement)
            {
                _parameterMap = parameterMap;
                _instance = instance;
                _instanceReplacement = instanceReplacement;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (_parameterMap.TryGetValue(node, out var replacement))
                {
                    return replacement;
                }
                return base.VisitParameter(node);
            }

            protected override Expression VisitConstant(ConstantExpression node)
            {
                if (node.Value == _instance)
                    return _instanceReplacement;

                if (node.Type.IsClass && node.Type.GetCustomAttribute<CompilerGeneratedAttribute>() != null)
                {
                    throw new InvalidOperationException(
                        "Closures over local variables are not supported in workflow match expressions. " +
                        "Expressions must rely exclusively on Signal, State, or the Workflow Instance (this).");
                }

                return base.VisitConstant(node);
            }
        }
    }
}
