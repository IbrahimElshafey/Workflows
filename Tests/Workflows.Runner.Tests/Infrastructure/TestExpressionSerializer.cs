using System;
using System.Linq.Expressions;

namespace Workflows.Runner.Tests.Infrastructure
{
    /// <summary>
    /// No-op expression serializer for testing (expressions are not serialized in tests)
    /// </summary>
    internal class TestExpressionSerializer : Workflows.Abstraction.Helpers.IExpressionSerializer
    {
        public object Serialize(LambdaExpression expression)
        {
            // In-memory tests don't need expression serialization
            return expression?.ToString() ?? string.Empty;
        }

        public LambdaExpression Deserialize(object serializedExpression)
        {
            // In-memory tests don't need expression deserialization
            throw new NotImplementedException("Expression deserialization not needed for in-memory tests");
        }
    }
}
