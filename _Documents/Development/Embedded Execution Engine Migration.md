# Embedded Execution Engine Migration Summary

This document summarizes the architectural redesign and implementation details for migrating from the distributed workflows architecture to the **Embedded Execution Engine** running in-process within the host application.

---

## 1. Architectural Changes & Topology

### 1.1 In-Process Channel Dispatching
- Communication between the host and the workflows engine is moved to **high-performance, in-memory `System.Threading.Channels`** (`WorkflowExecutionChannel`).
- The Workflow Engine maintains strict ACID boundaries for its internal SQLite tables (`WorkflowInstances`, `SignalWaits`, `OutboxMessages`, `CommandResults`) separate from the host application's domain context.
- Database access patterns ensure all slow/deferred processes strictly route through the transactional outbox/inbox tables.

### 1.2 Reflection-Free DI Command Registration
We replaced runtime generic and reflection-based resolution with **.NET Keyed Services** (`IServiceProvider.GetKeyedService`).

#### The Three Command Archetypes:
1. **Sync/Immediate (Type 1)**: Runs directly in the workflow thread. Registers `ICommandHandler<TInput, TOutput>` as a keyed service.
2. **Standard Async (Type 2)**: Segregated into `IDispatcher<TInput>` (to launch the task) and `IReceiver<TOutput>` (marker interface indicating the return payload type).
3. **External Async / Webhooks (Type 3)**: Registers a dispatcher, receiver, and a custom `LambdaExpression` match expression for callback correlation.

---

## 2. Dependency Injection API

A set of clean registration extensions are provided in `Workflows.Runner` on `IServiceCollection`:

```csharp
// Type 1: Sync Command
services.AddSyncCommand<TInput, TOutput, THandler>(commandKey);

// Type 2: Standard Async Command
services.AddStandardAsyncCommand<TInput, TOutput, TDispatcher, TReceiver>(commandKey);

// Type 3: External Async Command (Webhooks)
services.AddExternalAsyncCommand<TInput, TOutput, TCallbackPayload, TDispatcher, TReceiver>(
    commandKey, 
    (payload, input) => payload.CorrelationId == input.JobId
);
```

### Underlying Core Registry
- All metadata (types, execution modes, match expressions) is maintained in the singleton `CommandRegistryOptions`.
- `DiCommandHandlerFactory` resolves handlers using keyed services (`GetRequiredKeyedService`) on the hot path, eliminating runtime generic instantiation overhead.

---

## 3. Concurrency and Change-Tracking Fixes

### 3.1 SQLite Concurrency in `InboxPollerWorker`
- **Problem**: EF Core's change-tracker in `InboxPollerWorker` was tracking workflow execution entities across multiple iterations. Saving changes concurrently with `CoordinatorCommitWorker` caused `DbUpdateConcurrencyException`.
- **Solution**: Isolated the execution of `ProcessCommandResultAsync` inside its own short-lived service scope.
  ```csharp
  using (var execScope = _serviceProvider.CreateScope())
  {
      var execOrchestrator = execScope.ServiceProvider.GetRequiredService<Orchestrator>();
      await execOrchestrator.ProcessCommandResultAsync(dto);
  }
  ```

### 3.2 Integration Tests Scope Resolution
- **Problem**: In integration tests (`ClientIntegrationTests.cs`), the polling loop was using a single `IServiceScope` for the duration of the polling loop, meaning `workflowStore.GetInstanceStateAsync` served stale entities from EF's cache instead of reading the updated state from memory-backed SQLite.
- **Solution**: Replaced the outer scope with a scoped service resolution inside each polling iteration.

---

## 4. Performance & Redundant Wait Registrations

### 4.1 Double Mapping in Composite Waits
- **Problem**: Composite waits (`GroupWait` and `SubWorkflowWait`) were mapping child waits twice: once when registering the parent, and once when traversing child waits. This led to duplicate callback registrations in the `CallbackRegistry` and redundant template records in the SQLite template database cache.
- **Solution**: Added a `mapChildren` parameter toggle (default `true`) to `MapToDto` overloads in `Mapper.cs`. Parent processors now pass `mapChildren: false` during parent registration.
