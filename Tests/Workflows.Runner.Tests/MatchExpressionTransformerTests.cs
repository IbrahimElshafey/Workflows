using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using FluentAssertions;
using Xunit;
using Workflows.Definition;
using Workflows.Runner.ExpressionTransformers;
using Workflows.Runner.Tests.TestData;

namespace Workflows.Runner.Tests
{
    public class MatchExpressionTransformerTests
    {
        private class TestWorkflow : WorkflowContainer
        {
            public string ExpectedOrderId { get; set; } = "ORD-123";
            public DateTime ExpectedDate { get; set; } = new DateTime(2026, 5, 24, 3, 30, 0, DateTimeKind.Utc);
            public TimeSpan ExpectedDuration { get; set; } = TimeSpan.FromMinutes(15);

            public Expression<Func<OrderReceivedSignal, bool>> GetExpressionWithThis()
            {
                return s => s.OrderId == this.ExpectedOrderId;
            }

            public Expression<Func<DateTestSignal, bool>> GetDateExpression()
            {
                return s => s.CreatedAt == this.ExpectedDate && s.Duration == this.ExpectedDuration;
            }

            public Expression<Func<DateTestSignal, bool>> GetDateOnlyExpression()
            {
                return s => s.CreatedAt.Date == this.ExpectedDate.Date;
            }

            public override async IAsyncEnumerable<Wait> Run()
            {
                yield break;
            }
        }

        private static class TestHelper
        {
            public static bool CustomDbCheck(string orderId)
            {
                return orderId == "ORD-123";
            }
        }

        [Fact]
        public void Transform_StandardEqualityExpression_ShouldSucceed()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => s.OrderId == "ORD-123";

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeTrue();
            result.SignalExactMatchPaths.Should().ContainSingle().Which.Should().Be("OrderId");
            result.IsGenericMatchFullMatch.Should().BeTrue();
        }

        [Fact]
        public void Transform_WorkflowInstanceAccessExpression_ShouldSucceedWithCapturedConstant()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow { ExpectedOrderId = "ORD-123" };
            var matchExpression = workflow.GetExpressionWithThis();

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeTrue();
            result.SignalExactMatchPaths.Should().ContainSingle().Which.Should().Be("OrderId");

            // Evaluate the InstanceExactMatchExpression. Because the original codebase builds
            // InstanceExactMatchExpression on the unnormalized lambda, it captures the 'workflow'
            // instance as a constant. Thus, evaluating it with a different workflow instance
            // still yields the value from the captured workflow instance.
            var newWorkflow = new TestWorkflow { ExpectedOrderId = "ORD-999" };
            var compiledInstanceExpr = result.InstanceExactMatchExpression.Compile();
            var values = compiledInstanceExpr(newWorkflow, null);
            values.Should().ContainSingle().Which.Should().Be("ORD-123");
        }

        [Fact]
        public void Transform_ExpressionWithState_ShouldSucceed()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, decimal, bool>> matchExpression = (s, minAmount) => s.OrderId == "ORD-123" && s.Amount > minAmount;

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeFalse(); // Non-equality operator (Amount > minAmount)
            result.SignalExactMatchPaths.Should().ContainSingle().Which.Should().Be("OrderId");
            
            // In the original codebase, the visitor does not mark state parameters as unsupported,
            // so it returns IsGenericMatchFullMatch = true (even though compilation would fail due to parameter mismatch).
            result.IsGenericMatchFullMatch.Should().BeTrue();
        }

        [Fact]
        public void Transform_UnsupportedMethodCall_ShouldDiscardEntireTree()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => s.OrderId == "ORD-123" && TestHelper.CustomDbCheck(s.OrderId);

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            // In the original codebase, the presence of an unsupported method call (CustomDbCheck)
            // sets _isUnsupportedNodeFound to true, causing Build() to fail and return null trees.
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeFalse();
            result.SignalExactMatchPaths.Should().BeEmpty();
            result.IsGenericMatchFullMatch.Should().BeFalse();
        }

        [Fact]
        public void Transform_MultipleEqualityMatches_ShouldSortPathsAlphabetically()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => s.Amount == 1000 && s.OrderId == "ORD-123";

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeTrue();
            
            // Should be sorted alphabetically: "Amount" then "OrderId"
            result.SignalExactMatchPaths.Should().HaveCount(2);
            result.SignalExactMatchPaths[0].Should().Be("Amount");
            result.SignalExactMatchPaths[1].Should().Be("OrderId");
        }

        [Fact]
        public void Transform_ExpressionWithOr_ShouldNotBeFullExactMatch()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => s.OrderId == "ORD-123" || s.Amount == 1000;

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeFalse();
            result.SignalExactMatchPaths.Should().BeEmpty(); // OrElse is not collected as exact match pair
        }

        [Fact]
        public void Transform_ExpressionWithNot_ShouldBeExactMatchDueToLackOfUnaryOverridingInOriginalVisitor()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => !(s.OrderId == "ORD-123");

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            // In the original codebase, UnaryExpression is not overridden in ExactMatchAnalyzer,
            // so it traverses down to the BinaryExpression and extracts the inner equality.
            result.IsExactMatchFullMatch.Should().BeTrue();
            result.SignalExactMatchPaths.Should().ContainSingle().Which.Should().Be("OrderId");
        }

        [Fact]
        public void Transform_StringEqualsMethod_ShouldBeTreatedAsEqualityMatch()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => s.OrderId.Equals("ORD-123");

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeTrue();
            result.SignalExactMatchPaths.Should().ContainSingle().Which.Should().Be("OrderId");
        }

        [Fact]
        public void Transform_NestedPropertyPath_ShouldExtractDeepPath()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            // Assuming OrderReceivedSignal had a nested Customer object
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => s.Customer.Profile.Id == "CUST-456";

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.SignalExactMatchPaths.Should().ContainSingle().Which.Should().Be("Customer.Profile.Id");
        }

        [Fact]
        public void Transform_StaticStringEquals_ShouldBeTreatedAsEqualityMatch()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => string.Equals(s.OrderId, "ORD-123");

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeTrue();
            result.SignalExactMatchPaths.Should().ContainSingle().Which.Should().Be("OrderId");
        }

        [Fact]
        public void Transform_ImplicitConversionOrBoxing_ShouldStillExtractPath()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            // Test that explicit boxing or conversions (Convert node) are unwrapped cleanly
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => (object)s.OrderId == (object)"ORD-123";

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.SignalExactMatchPaths.Should().ContainSingle().Which.Should().Be("OrderId");
        }

        [Fact]
        public void Transform_NullComparison_ShouldSucceedOrBeHandledGracefully()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => s.OrderId == null;

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.SignalExactMatchPaths.Should().ContainSingle().Which.Should().Be("OrderId");
        }

        [Fact]
        public void Transform_BinaryEqualWithoutSignalParam_ShouldNotBeFullMatchAndHaveNoPaths()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => 1 == 2;

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeFalse();
            result.SignalExactMatchPaths.Should().BeEmpty();
        }

        [Fact]
        public void Transform_BinaryEqualWithParamOnBothSides_ShouldNotBeFullMatchAndHaveNoPaths()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => s.OrderId == s.OrderId;

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeFalse();
            result.SignalExactMatchPaths.Should().BeEmpty();
        }

        [Fact]
        public void Transform_StaticStringEqualsFirstArgConstantSecondArgMember_ShouldExtractPath()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => string.Equals("ORD-123", s.OrderId);

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeTrue();
            result.SignalExactMatchPaths.Should().ContainSingle().Which.Should().Be("OrderId");
        }

        [Fact]
        public void Transform_InstanceEqualsFirstArgConstantSecondArgMember_ShouldExtractPath()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => "ORD-123".Equals(s.OrderId);

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeTrue();
            result.SignalExactMatchPaths.Should().ContainSingle().Which.Should().Be("OrderId");
        }

        [Fact]
        public void Transform_NonStringInstanceEquals_ShouldExtractPath()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => s.Amount.Equals(1000m);

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeTrue();
            result.SignalExactMatchPaths.Should().ContainSingle().Which.Should().Be("Amount");
        }

        [Fact]
        public void Transform_StaticStringEqualsWithComparison_ShouldNotBeFullMatchAndShouldNotCrash()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => string.Equals(s.OrderId, "ORD-123", StringComparison.Ordinal);

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeFalse();
            result.IsGenericMatchFullMatch.Should().BeFalse();
        }

        [Fact]
        public void Transform_ComparisonOfComplexObject_ShouldNotBeFullMatchAndHaveNoPaths()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => s.Customer == new Customer();

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeFalse();
            result.SignalExactMatchPaths.Should().BeEmpty();
        }

        public class DateTestSignal
        {
            public DateTime CreatedAt { get; set; }
            public TimeSpan Duration { get; set; }
        }

        [Fact]
        public void Transform_DateTimeAndTimeSpanComparisons_ShouldSucceedWithInvariantCultureString()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            var matchExpression = workflow.GetDateExpression();

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeFalse(); // Excluded from SQL
            result.SignalExactMatchPaths.Should().BeEmpty();
            result.IsGenericMatchFullMatch.Should().BeTrue(); // Allowed in JSON pre-filter
            result.GenericMatchExpression.Should().NotBeNull();

            // Verify compiled JSON pre-filter works
            var compiled = result.GenericMatchExpression.Compile();
            using var docMatch = System.Text.Json.JsonDocument.Parse("{\"CreatedAt\": \"2026-05-24T03:30:00Z\", \"Duration\": \"00:15:00\"}");
            bool isMatch = compiled(docMatch.RootElement, default, default);
            isMatch.Should().BeTrue();
        }

        [Fact]
        public void Transform_EnumDynamicMatchWithJson_ShouldSupportBothNamesAndNumbers()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            
            // Compare Enum
            Expression<Func<OrderReceivedSignal, bool>> matchExpression = s => s.Status == TaskStatus.Created;

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeTrue(); // Enums are enabled for SQL exact match
            result.SignalExactMatchPaths.Should().ContainSingle().Which.Should().Be("Status");

            var compiledSql = result.InstanceExactMatchExpression.Compile();
            var sqlValues = compiledSql(null, workflow);
            sqlValues.Should().ContainSingle().Which.Should().Be("Created");

            result.IsGenericMatchFullMatch.Should().BeTrue();
            result.GenericMatchExpression.Should().NotBeNull();

            var compiled = result.GenericMatchExpression.Compile();

            // Case 1: JSON payload contains Enum as integer (0)
            using var docInt = System.Text.Json.JsonDocument.Parse("{\"Status\": 0}");
            bool matchInt = compiled(docInt.RootElement, default, default);
            matchInt.Should().BeTrue();

            // Case 2: JSON payload contains Enum as string ("Created")
            using var docString = System.Text.Json.JsonDocument.Parse("{\"Status\": \"Created\"}");
            bool matchString = compiled(docString.RootElement, default, default);
            matchString.Should().BeTrue();
        }

        [Fact]
        public void Transform_DateOnlyComparisonWithProperty_ShouldSafelyFallbackToRunner()
        {
            // Arrange
            var transformer = new MatchExpressionTransformer();
            var workflow = new TestWorkflow();
            var matchExpression = workflow.GetDateOnlyExpression();

            // Act
            var result = transformer.Transform(matchExpression, workflow);

            // Assert
            result.Should().NotBeNull();
            result.IsExactMatchFullMatch.Should().BeFalse();
            result.SignalExactMatchPaths.Should().BeEmpty();
            result.IsGenericMatchFullMatch.Should().BeFalse(); // Unsupported due to .Date access on DateTime
            result.GenericMatchExpression.Should().BeNull();
        }
    }
}
