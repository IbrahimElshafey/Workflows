# Workflows.Abstraction

## 1. What is this?
The central contract and Data Transfer Object (DTO) layer (targeting `netstandard2.1`) for the workflows engine. It defines the interfaces and data structures that bind the runner, orchestrator and external clients together, providing clean dependency injection boundaries.

Key interfaces defined:
- `IOrchestrator` (namespace `Workflows.Abstraction.Orchestrator`): `StartWorkflowAsync`, `ProcessSignalAsync`, `ProcessCommandResultAsync`, `CancelWorkflowAsync`.
- `IWorkflowRunner` & `IWorkflowRunnerClient`: drive and dispatch state machine execution.
- `IWorkflowStore` (`Workflows.Abstraction.Persistence`): the persistence contract — instance snapshots, signal inbox, wait indexes, timers and worker capabilities.
- `IInstanceLockManager`: row-level instance locks with TTL expiry.
- `IExternalScheduler`: schedules and cancels timer signals.
- `IObjectSerializer` & `IExpressionSerializer`: serializers for state snapshots and match expressions.

## 2. How to use?
Reference this project to write integrations, custom persistence adapters, or to interact with the engine through its boundaries.

### The persistence contract

`IWorkflowStore` is deliberately small in surface but transactional in intent — a custom adapter must implement all of it:

```csharp
public interface IWorkflowStore
{
    // Snapshot persistence + routing-table sync (MUST be atomic)
    Task SaveContextSyncAsync(WorkflowStateDto state, IEnumerable<string> completedWaitIds, Guid? triggeringSignalId = null);
    Task<WorkflowStateDto> GetInstanceStateAsync(Guid instanceId);

    // Idempotency
    Task<bool> HasSignalBeenProcessedAsync(Guid messageId);
    Task PruneProcessedSignalsAsync(DateTime threshold);

    // Routing lookups
    Task<List<Guid>> FindInstancesWaitingForSignalAsync(string signalPath, string signalDataJson);
    Task<Guid> GetInstanceByCommandWaitIdAsync(string commandWaitId);
    Task<List<TimeWaitDto>> GetPendingTimeWaitsAsync();

    // Migration
    Task ReplaceMigratedStateAsync(Guid instanceId, WorkflowStateDto newState,
        List<WaitInfrastructureDto> newWaits, int newVersion, CancellationToken ct);

    // Worker capability routing / drain
    Task RegisterWorkerCapabilitiesAsync(string dllVersion,
        IEnumerable<(string workflowType, int workflowVersion)> capabilities, CancellationToken ct = default);
    Task<List<string>> GetActiveDllVersionsForWorkflowAsync(string workflowType, int workflowVersion, CancellationToken ct = default);
    Task<Dictionary<string, int>> GetActiveInstanceCountsByDllVersionAsync(CancellationToken ct = default);

    // Clustered timers
    Task<List<TimeWaitDto>> ClaimDueTimersAsync(int batchSize, string nodeId, CancellationToken ct = default);
    Task RecoverStaleTimersAsync(TimeSpan staleThreshold, CancellationToken ct = default);
}
```

The shipping implementation is `Workflows.Storage.EntityFrameworkCore.WorkflowStore` (`DataStore/Workflows.Storage.EntityFrameworkCore/WorkflowStore.cs`); the SQLite, SQL Server and PostgreSQL packages differ only in EF Core provider registration.