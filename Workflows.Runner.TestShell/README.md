# Workflows.Runner.TestShell

## 1. What is this?
A unit and integration testing harness library (targeting `net10.0`) that simplifies validating workflow containers in-memory.

Key features:
- **`WorkflowTestShell`**: The primary class which creates an isolated, in-memory running context for workflows.
- **Executor Mocking**: Mock out external client/worker command executors and assert that specific commands were triggered with expected parameters.
- **Signal Simulation**: Programmatically fire signals into workflows and inspect state updates synchronously.

## 2. How to use?
Reference this project in your unit test projects to test workflow definitions without spinning up databases, web APIs, or messaging queues.

### Example (xUnit Test):
```csharp
using System.Threading.Tasks;
using Xunit;
using Workflows.Runner.TestShell;

public class MyWorkflowTests
{
    [Fact]
    public async Task Workflow_ShouldAdvance_OnSignal()
    {
        // 1. Arrange - Initialize the test shell and register workflow
        using var shell = new WorkflowTestShell();
        shell.RegisterWorkflow<OrderWorkflow>("OrderWorkflow", "1.0.0");

        // 2. Act - Start the workflow and post a signal
        await shell.StartWorkflowAsync("OrderWorkflow");
        await shell.SimulateSignalAsync("OrderSubmitted", new OrderSubmittedEvent { Total = 150 });

        // 3. Assert - Verify workflow state and history logs
        Assert.Contains(shell.ExecutionLog, log => log.Description.Contains("OrderSubmitted"));
        Assert.True(shell.IsWorkflowCompleted);
    }
}
```
