# Workflows.Runner.TestShell

## 1. What is this?
A unit and integration testing harness (targeting `net10.0`) that runs workflow containers in memory — no database, web API or message broker required.

Key features:
- **`WorkflowTestShell`** (namespace `Workflows.TestShell`): builds an isolated in-memory engine around one or more workflows.
- **Command mocking**: `SetupCommandHandler<TCommand, TResult>(...)` stubs command execution and records the dispatched commands.
- **Signal simulation**: fire signals or command results and inspect the resulting state, waits and execution log synchronously.

## 2. How to use?
Reference `Workflows.Runner.TestShell` from your test project.

### Example (xUnit test):

```csharp
using System.Linq;
using System.Threading.Tasks;
using Workflows.TestShell;
using Xunit;

public class MyWorkflowTests
{
    [Fact]
    public async Task Workflow_ShouldAdvance_OnSignal()
    {
        // 1. Arrange
        using var shell = new WorkflowTestShell();
        shell.RegisterWorkflow<OrderWorkflow>("OrderWorkflow", 1);   // version is an int
        shell.RegisterSignal<OrderSubmittedEvent>("OrderSubmitted");

        // 2. Act
        await shell.StartWorkflowAsync("OrderWorkflow", input: null);
        await shell.SimulateSignalAsync("OrderSubmitted", new OrderSubmittedEvent { Total = 150 });

        // 3. Assert
        Assert.Contains(shell.ActiveWaits, w => w.WaitName == "WaitPayment");
        Assert.Equal(WorkflowInstanceStatus.InProgress, shell.CurrentStatus);
        Assert.NotEmpty(shell.ExecutionLog);
    }
}
```

### Reading results

| Member | Contents |
| :--- | :--- |
| `CurrentState` | The latest `WorkflowStateDto` (state object, waits, status). |
| `CurrentStatus` | `WorkflowInstanceStatus` of the latest snapshot. |
| `ActiveWaits` | Waits whose status is `WaitStatus.Waiting`. |
| `ExecutionLog` | `ExecutionLogEntry` per engine tick: `TriggeringWaitId`, `ConsumedWaitIds`, `NewWaitIds`, `Status`, `SerializedStateSnapshot`, `Timestamp`. |

There is no `IsWorkflowCompleted` flag — assert on `CurrentStatus == WorkflowInstanceStatus.Completed` instead. `ExecutionLogEntry` has no `Description`; use `TriggeringWaitId` / `NewWaitIds` or the serialized snapshot for diagnostics.

Useful simulation helpers: `SimulateSignalAsync(id, data)`, `SimulateCommandResultAsync(commandIdentifier, result)`, `SimulateCommandResultByWaitIdAsync(waitId, result)` and `SimulateMatchAsync(waitDto, signalOrResult)`.