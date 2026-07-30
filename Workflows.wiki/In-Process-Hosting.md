# In-Process & Worker Hosting

The engine features a built-in hosting model under `Workflows.Hosting.InProcess` that runs the orchestrator, runner, database persistence, and background timer scheduler together inside a single process, as well as out-of-process worker process supervision options for isolated side-by-side execution.

---

## 1. In-Process Monolithic Setup

To configure the in-process host, add the `AddWorkflowsInProcessHost` extension to your ASP.NET Core or generic host service configuration:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Workflows.Hosting.InProcess;

var services = new ServiceCollection();

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

* **Database Storage Provider:** Configures Entity Framework Core mappings and target RDBMS driver.
* **In-Process Message Transport:** Creates a loopback communication queue (`InProcessMessageTransport`) for request-response signaling between the orchestrator and runner.
* **In-Memory Channels & Hosted Workers:** Sets up `WorkflowExecutionChannel` (using `System.Threading.Channels`) and background hosted services like `RunnerWorker` to process execution tasks asynchronously without network overhead.
* **Background Scheduler:** Spins up a background `IHostedService` (`Scheduler`) to query database timers and dispatch signals when delays expire.
* **Runner Proxy:** Configures the client to route execution requests through the loopback bus to the stateless `Runner` logic.

---

## 5. Controlling Workflows

Once the host is registered, inject and use `IOrchestrator` to coordinate workflows:

### Starting a Workflow
```csharp
var orchestrator = serviceProvider.GetRequiredService<IOrchestrator>();

var workflowId = Guid.NewGuid();
var startRequest = new StartWorkflowRequest
{
    InstanceId = workflowId,
    WorkflowName = "OrderProcessingWorkflow",
    Version = 1,
    State = new OrderState { MinOrderId = 0 } // Initial state payload
};

await orchestrator.StartWorkflowAsync(startRequest);
```

### Posting a Signal to a Pending Workflow
```csharp
var signalEvent = new OrderSubmittedEvent
{
    OrderId = 101,
    CustomerName = "Alice"
};

// Orchestrator locates instance waiting on signal path, hydrates state, runs step, and commits transaction
await orchestrator.PostSignalAsync(
    signalPath: "OrderSubmittedSignal",
    signalPayload: signalEvent
);
```
