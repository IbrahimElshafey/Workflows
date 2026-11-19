# Workflows.Orchestrator

## 1. What is this?
The control plane orchestrator (targeting `net10.0`) of the workflows engine. It is the sole owner of database transactions: it routes signals and command results, persists execution state, and fires delayed steps through the timer engine.

Key responsibilities:
- **Instance Coordination**: starts instances, applies signals, command results, cancellation and compensation, and commits the resulting state atomically.
- **Scheduler**: `Scheduler` implements `IExternalScheduler` and `IHostedService` — an in-memory fast path plus a clustered DB poller (`ClaimDueTimersAsync`) that recovers stale claims.
- **Persistence Handling**: drives `IWorkflowStore` (SQLite / SQL Server / PostgreSQL) to load and commit snapshots.
- **Runner Proxying**: dispatches execution requests to the `Workflows.Runner` over the configured transport.

## 2. How to use?
Inject `IOrchestrator` (namespace `Workflows.Abstraction.Orchestrator`) into your controllers, web APIs or message handlers:

```csharp
using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Orchestrator;

public class OrderController
{
    private readonly IOrchestrator _orchestrator;

    public OrderController(IOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task<Guid> StartOrderProcess(decimal amount)
    {
        // Returns the new WorkflowInstanceId
        return await _orchestrator.StartWorkflowAsync(
            workflowName: "OrderProcessingWorkflow",
            version: 1,
            input: new { Amount = amount });
    }

    public Task Cancel(Guid instanceId) =>
        _orchestrator.CancelWorkflowAsync(instanceId, token: "user-cancel", reason: "Requested by customer");

    public Task SubmitSignal(SignalDto signal) =>
        _orchestrator.ProcessSignalAsync(signal);
}
```

Version is an `int`, not a semantic version string, and the caller does not supply the instance ID — the engine generates it and returns it from `StartWorkflowAsync`.