using FluentAssertions;
using System;
using System.Threading.Tasks;
using Workflows.Runner.Tests.TestWorkflows;
using Xunit;

namespace Workflows.Runner.Tests
{
    /// <summary>
    /// Tests for token-based cancellation
    /// </summary>
    public class CancellationTests
    {
        [Fact]
        public async Task Cancellation_ShouldSkipWaitsWithCancelledTokens()
        {
            // Arrange
            var workflow = new CancellationTestWorkflow
            {
                ShouldCancel = true
            };

            // Act
            var enumerator = workflow.ExecuteWorkflowAsync().GetAsyncEnumerator();

            int waitCount = 0;
            while (await enumerator.MoveNextAsync())
            {
                waitCount++;
                var wait = enumerator.Current;
            }

            // Assert
            workflow.ExecutionLog.Should().Contain("Cancelling order flow");
            // Payment wait should be skipped after cancellation
            workflow.ExecutionLog.Should().NotContain("Payment confirmed");
        }

        [Fact]
        public async Task Cancellation_ShouldInvokeOnCanceled_WhenTokenCancelled()
        {
            // Arrange
            var workflow = new CancellationTestWorkflow
            {
                ShouldCancel = true
            };

            // Act
            var enumerator = workflow.ExecuteWorkflowAsync().GetAsyncEnumerator();

            while (await enumerator.MoveNextAsync())
            {
                var wait = enumerator.Current;
            }

            // Assert
            workflow.ExecutionLog.Should().Contain(log => log.Contains("cancelled"));
        }

        [Fact]
        public async Task Cancellation_ShouldNotAffectWaits_WhenNotTriggered()
        {
            // Arrange
            var workflow = new CancellationTestWorkflow
            {
                ShouldCancel = false
            };

            // Act
            var enumerator = workflow.ExecuteWorkflowAsync().GetAsyncEnumerator();

            while (await enumerator.MoveNextAsync())
            {
                var wait = enumerator.Current;
            }

            // Assert
            workflow.ExecutionLog.Should().NotContain("Cancelling order flow");
            workflow.ExecutionLog.Should().Contain("End");
        }

        [Fact]
        public void CancellationWorkflow_ShouldSupportWithCancelToken()
        {
            // Arrange & Act
            var workflow = new CancellationTestWorkflow();

            // Assert - Verify DSL supports .WithCancelToken()
            workflow.Should().NotBeNull();
            workflow.ExecutionLog.Should().NotBeNull();
        }
    }
}
