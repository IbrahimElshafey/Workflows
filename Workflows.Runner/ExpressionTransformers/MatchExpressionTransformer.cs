using System;
using Workflows.Handler.BaseUse;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class MatchExpressionTransformer
    {
        internal MatchTransformationResult Transform(Wait wait)
        {
            var type = wait.GetType();

            // Check if it's specifically a SignalWait<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SignalWait<>))
            {
                // Use 'dynamic' to let the DLR find the correct TransformInternal<T> at runtime
                return TransformInternal((dynamic)wait);
            }

            throw new ArgumentException($"Expected SignalWait<T> but received {type.Name}");
        }
        private MatchTransformationResult TransformInternal<T>(SignalWait<T> signalWait)
        {
            // Inside here, T is correctly identified (e.g., User, Order, etc.)
            // You can now manipulate signalWait.Expression safely
            return new MatchTransformationResult();
        }
    }
}
