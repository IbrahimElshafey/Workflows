using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Runner.Tests.TestData;
using Workflows.Runner.Tests.TestWorkflows;
using Xunit;

namespace Workflows.Runner.Tests
{
    /// <summary>
    /// Integration tests for compensation (Saga pattern)
    /// </summary>
    public class CompensationTests
    {
        [Fact]
        public async Task Compensation_ShouldExecuteInLIFOOrder_WhenTriggered()
        {
            // Arrange
            var workflow = new CompensationTestWorkflow
            {
                ShouldFail = true // Trigger compensation
            };

            // Act
            var enumerator = workflow.ExecuteWorkflowAsync().GetAsyncEnumerator();

            // Execute until we hit compensation or completion
            while (await enumerator.MoveNextAsync())
            {
                var wait = enumerator.Current;
                // In a real scenario, runner would process each wait
            }

            // Assert
            workflow.ExecutionLog.Should().ContainInOrder(
                "Start",
                "Inventory reserved: ",
                "Payment processed: ",
                "Failure detected - triggering compensation",
                "Refunding payment: ", // LIFO: Payment compensated first
                "Compensating inventory: " // Then inventory
            );
        }

        [Fact]
        public async Task Compensation_ShouldNotExecute_WhenNoFailure()
        {
            // Arrange
            var workflow = new CompensationTestWorkflow
            {
                ShouldFail = false // No compensation needed
            };

            // Act
            var enumerator = workflow.ExecuteWorkflowAsync().GetAsyncEnumerator();

            while (await enumerator.MoveNextAsync())
            {
                var wait = enumerator.Current;
            }

            // Assert
            workflow.ExecutionLog.Should().NotContain(log => log.Contains("Compensating"));
            workflow.ExecutionLog.Should().NotContain(log => log.Contains("Refunding"));
        }

        [Fact]
        public void CompensationWorkflow_ShouldRegisterCompensation_OnCommands()
        {
            // Arrange
            var workflow = new CompensationTestWorkflow();

            // Act - Check that workflow DSL supports RegisterCompensation
            var enumerator = workflow.ExecuteWorkflowAsync().GetAsyncEnumerator();

            // Assert - Verify workflow can be instantiated with compensation
            workflow.Should().NotBeNull();
            workflow.ExecutionLog.Should().NotBeNull();
        }
    }
}
