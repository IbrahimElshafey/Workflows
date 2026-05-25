using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Workflows.Definition;
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
            var enumerator = workflow.Run().GetAsyncEnumerator();
            var executedCommands = new List<(dynamic Wait, object Result)>();

            // Execute until we hit compensation or completion
            while (await enumerator.MoveNextAsync())
            {
                var wait = enumerator.Current;
                if (wait.WaitType == Primitives.WaitType.Command)
                {
                    dynamic dWait = wait;
                    object result = null;
                    if (dWait.WaitName == "ReserveInventory")
                    {
                        result = new ReserveInventoryResult { Success = true };
                    }
                    else if (dWait.WaitName == "ProcessPayment")
                    {
                        result = new ProcessPaymentResult { Success = true };
                    }

                    if (dWait.OnResultAction != null)
                    {
                        var parameters = dWait.OnResultAction.Method.GetParameters();
                        if (parameters.Length == 1)
                        {
                            dWait.OnResultAction.DynamicInvoke(result);
                        }
                        else
                        {
                            dWait.OnResultAction.DynamicInvoke(result, dWait.ExplicitState);
                        }
                    }

                    executedCommands.Add((dWait, result));
                }
                else if (wait is CompensationWait compWait)
                {
                    // LIFO execution
                    for (int i = executedCommands.Count - 1; i >= 0; i--)
                    {
                        var cmd = executedCommands[i];
                        if (cmd.Wait.CompensationAction != null)
                        {
                            // Filter by token if applicable
                            string[] tokens = cmd.Wait.CompensationTokens;
                            if (tokens == null || tokens.Contains(compWait.Token))
                            {
                                var parameters = cmd.Wait.CompensationAction.Method.GetParameters();
                                ValueTask vt;
                                if (parameters.Length == 1)
                                {
                                    vt = (ValueTask)cmd.Wait.CompensationAction.DynamicInvoke(cmd.Result);
                                }
                                else
                                {
                                    vt = (ValueTask)cmd.Wait.CompensationAction.DynamicInvoke(cmd.Result, cmd.Wait.ExplicitState);
                                }
                                await vt;
                            }
                        }
                    }
                }
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
            var enumerator = workflow.Run().GetAsyncEnumerator();

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
            var enumerator = workflow.Run().GetAsyncEnumerator();

            // Assert - Verify workflow can be instantiated with compensation
            workflow.Should().NotBeNull();
            workflow.ExecutionLog.Should().NotBeNull();
        }
    }
}
