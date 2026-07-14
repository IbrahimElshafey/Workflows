# In-Process Hosting

The engine features a built-in hosting model under `Workflows.Hosting.InProcess` that runs the orchestrator, runner, database persistence, and background timer scheduler together inside a single process.

---

## 1. Setting Up the Dependency Injection

To configure the in-process host, add the `AddWorkflowsInProcessHost` extension to your service configuration:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Workflows.Hosting.InProcess;

var services = new ServiceCollection();

// Configures the complete workflow engine with a SQLite database
services.AddWorkflowsInProcessHost("Data Source=workflows.db");
```

---

## 2. Architectural Components Registered

When calling `AddWorkflowsInProcessHost`, the engine registers:

*   **SQLite Database Provider:** Sets up the Entity Framework Core migrations and SQLite connection.
*   **In-Process Message Transport:** Creates a loopback communication queue (`InProcessMessageTransport`) for request-response signaling between the orchestrator and the runner.
*   **In-Memory Channels & Hosted Workers:** Sets up `WorkflowExecutionChannel` (using `System.Threading.Channels`) and background hosted services like `RunnerWorker` to process execution tasks asynchronously without network overhead.
*   **Background Scheduler:** Spins up a background .NET `IHostedService` (`Scheduler`) to query database timers and dispatch signals when delays expire.
*   **Runner Proxy:** Configures the client to route execution requests through the loopback bus to the stateless `Runner` logic.

---

## 3. Controlling Workflows

Once the host is registered, you can inject and use `IOrchestrator` to coordinate workflows.

### Starting a Workflow
```csharp
var orchestrator = serviceProvider.GetRequiredService<IOrchestrator>();

var workflowId = Guid.NewGuid();
var startRequest = new StartWorkflowRequest
{
    InstanceId = workflowId,
    WorkflowName = "OrderProcessingWorkflow",
    Version = 1,
    State = new OrderState { MinOrderId = 0 } // Workflow initial state argument
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

// Orchestrator finds the instance waiting on this signal path, Hydrates, Runs, and Commits
await orchestrator.PostSignalAsync(
    signalPath: "OrderSubmittedSignal",
    signalPayload: signalEvent
);
```
