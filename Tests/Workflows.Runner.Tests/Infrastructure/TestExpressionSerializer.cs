using System.Linq.Expressions;

namespace Workflows.Runner.Tests.Infrastructure
{
    /// <summary>
    /// In-memory expression serializer for testing (expressions are kept in memory)
    /// </summary>
    internal class TestExpressionSerializer : Workflows.Abstraction.Helpers.IExpressionSerializer
    {
        public object Serialize(LambdaExpression expression)
        {
            return expression;
        }

        public LambdaExpression Deserialize(object serializedExpression)
        {
            return serializedExpression as LambdaExpression;
        }
    }
}
