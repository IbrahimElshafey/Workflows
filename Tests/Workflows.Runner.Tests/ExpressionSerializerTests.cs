using System;
using System.Linq;
using System.Linq.Expressions;
using Workflows.Shared.Serialization;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class ExpressionSerializerTests
    {
        // Standard POCO state object with no complex nested generic ASTs
        public class WorkflowState
        {
            public string StateName { get; set; }
            public int Amount { get; set; }
        }

        [Fact]
        public void Serialize_StateObject_ContainsValues()
        {
            // Arrange
            var serializer = new ExpressionSerializer();
            var state = new WorkflowState { StateName = "Processing", Amount = 500 };

            // Act: Build the expression manually to avoid hidden compiler closures
            var outerExpr = Expression.Lambda<Func<WorkflowState>>(Expression.Constant(state));

            var serializedResult = serializer.Serialize(outerExpr);
            string jsonString = serializedResult.ToString();

            // Assert: Using your suggested checks to verify the actual data is preserved!
            Assert.Contains("\"Processing\"", jsonString);
            Assert.Contains("500", jsonString);
        }

        [Fact]
        public void Deserialize_NestedExpression_RoundTripsCorrectly()
        {
            // Arrange
            var serializer = new ExpressionSerializer();
            Expression<Func<int, bool>> nestedExpr = x => x == 42;

            // Act: Store the nested expression directly as a constant to perfectly trigger our fix
            var outerExpr = Expression.Lambda<Func<Expression>>(Expression.Constant(nestedExpr, typeof(Expression)));

            var serialized = serializer.Serialize(outerExpr);
            var deserialized = serializer.Deserialize(serialized);

            // Assert
            Assert.NotNull(deserialized);

            // Extract and compile the outer expression to get the inner expression back
            var compiledOuter = (Func<Expression>)deserialized.Compile();
            var extractedExpr = (Expression<Func<int, bool>>)compiledOuter();

            // Compile and execute the inner nested lambda that was safely round-tripped
            var compiledInner = extractedExpr.Compile();
            Assert.True(compiledInner(42));
            Assert.False(compiledInner(10));
        }
    }
}
