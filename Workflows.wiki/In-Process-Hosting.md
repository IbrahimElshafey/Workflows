# In-Process & Worker Hosting

The engine features a built-in hosting model under `Workflows.Hosting.InProcess` that runs the orchestrator, runner, database persistence, and background timer scheduler together inside a single process, as well as out-of-process worker process supervision options for isolated side-by-side execution.

---

## 1. In-Process Monolithic Setup

To configure the in-process host, add the `AddWorkflowsInProcessHost` extension to your ASP.NET Core or generic host service configuration:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Workflows.Hosting.InProcess;
using Workflows.Runner;    // AddWorkflowsRunner
using Workflows.Shared;    // AddWorkflowsShared

var services = new ServiceCollection();

// Shared serializers + stateless runner
services.AddWorkflowsShared();
services.AddWorkflowsRunner();

// Configures the complete workflow engine with a SQLite database
services.AddWorkflowsInProcessHost("Data Source=workflows.db");
```

### Alternative Database Storage Adapters:
* **PostgreSQL:** `services.AddWorkflowsPostgres("Host=localhost;Database=workflows;Username=postgres;Password=secret")`
* **Microsoft SQL Server:** `services.AddWorkflowsSqlServer("Server=localhost;Database=WorkflowsDB;Trusted_Connection=True;")`

---

## 2. Embedded Admin Web UI Integration

Embed the administration dashboard into your ASP.NET Core application host:

```csharp
using Workflows.Admin.UI;
using Workflows.Admin.UI.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register Admin UI with database provider
builder.Services.AddWorkflowsAdminUI(connectionString, options =>
{
    options.RoutePrefix = "admin";
    options.Provider = AdminDbProvider.Sqlite;
    options.EnableWriteActions = true;
});

var app = builder.Build();

app.UseRouting();
app.UseWorkflowsAdminUI(); // Maps /admin endpoints and embedded static assets
app.Run();
```

---

## 3. Out-of-Process Worker Supervision (`WorkerProcessSupervisor`)

For multi-version Side-by-Side (SxS) execution or isolated compute worker nodes, use `WorkerProcessSupervisor` to launch worker sub-processes managed via Named Pipe IPC:

```csharp
using Workflows.Hosting.InProcess;

// Instantiate supervisor pointing to worker binary
var supervisor = new WorkerProcessSupervisor("Workflows.Runner.dll");

// Spawn and complete handshake with worker process V1.0.0
var workerInfo = await supervisor.EnsureWorkerAsync("1.0.0", assemblyPath: null, ct: cancellationToken);

// Dispatch IPC commands to worker process
bool success = await supervisor.SendIpcCommandAsync("1.0.0", "Handshake", "", cancellationToken);

// Graceful shutdown
await supervisor.ShutdownWorkerAsync("1.0.0", cancellationToken);
```

---

## 4. Architectural Components Registered

When calling `AddWorkflowsInProcessHost`, the engine registers:

* **Database Storage Provider:** Configures EF Core mappings and the SQLite RDBMS driver (`AddWorkflowsSqlite`).
* **In-Process Message Transport:** Creates a loopback communication queue (`InProcessMessageTransport` / `InProcessMessageSubscriber`) with default routing plus explicit rules for `StartWorkflowRequest` and `WorkflowExecutionRequest`.
* **In-Memory Channels & Hosted Workers:** Registers `WorkflowExecutionChannel`, `BackgroundWorkerChannel` and the hosted services `RunnerWorker`, `CoordinatorCommitWorker`, `CommandExecutorWorker`, `CompensationWorker`, `CancelerWorker`, `InboxPollerWorker` and `LockExpiryWorker`.
* **Background Scheduler:** Registers `Scheduler` as both `IExternalScheduler` and `IHostedService`; it runs the in-memory fast path and the clustered DB timer poller.
* **Instance Cache & Outbox:** `WorkflowInstanceCache` and `OutboxNotificationDispatcher` for hot-state caching and deferred command dispatch.
* **Runner Proxy:** Registers `IWorkflowRunner` as `RunnerProxy` and `IWorkflowRunnerClient` as `ChannelWorkflowRunnerClient`, so execution requests flow through the loopback bus into the stateless runner.
* **Orchestrator:** Registers `Orchestrator` and exposes it as `IOrchestrator` through `InboxOrchestrator` (idempotent inbox + outbox commit).

---

## 5. Controlling Workflows

Once the host is registered, inject and use `IOrchestrator` to coordinate workflows:

### Starting a Workflow
```csharp
using Workflows.Abstraction.Orchestrator;

var orchestrator = serviceProvider.GetRequiredService<IOrchestrator>();

// The engine generates the InstanceId and returns it.
// 'input' is the workflow's start payload, not its state object.
Guid instanceId = await orchestrator.StartWorkflowAsync(
    workflowName: "OrderProcessingWorkflow",
    version: 1,
    input: new { MinOrderId = 0 });
```

### Posting a Signal to a Pending Workflow
```csharp
using Workflows.Abstraction.DTOs;

var signal = new SignalDto
{
    Id = Guid.NewGuid(),
    SignalIdentifier = "OrderSubmittedSignal",
    Data = new OrderSubmittedEvent { OrderId = 101, CustomerName = "Alice" },
    ClientSentTime = DateTime.UtcNow
};

// Orchestrator locates the instance waiting on the signal path,
// hydrates state, runs the step and commits the transaction.
await orchestrator.ProcessSignalAsync(signal);
```

### Cancelling an Instance
```csharp
await orchestrator.CancelWorkflowAsync(
    instanceId,
    token: "user-cancel",
    reason: "Requested by customer");
```

> `StartWorkflowRequest` and `PostSignalAsync(signalPath:, signalPayload:)` do not exist on `IOrchestrator` — instance identity is assigned by the engine, and signals are submitted as `SignalDto` through `ProcessSignalAsync`.
