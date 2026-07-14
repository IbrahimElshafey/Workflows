# Workflow Messaging Architecture

This document outlines the communication architecture between the **Workflow Orchestrator** and the **Workflow Runners** in the embedded in-process execution model.

---

## 1. The Core Interfaces

The communication layer is built on three primary interfaces, completely fire-walling the engine logic from the underlying transport mechanics.

### `IMessageTransport` (The Sender Engine)
Implemented by physical transports. It requires a physical destination address.
```csharp
public interface IMessageTransport
{
    Task SendAsync<T>(string destination, T message);
    Task<TResponse> SendAndReceiveAsync<TRequest, TResponse>(string destination, TRequest message);
}
```

### `IMessageSubscriber` (The Listener Engine)
Implemented by physical listeners. It binds a physical address to a C# execution handler.
```csharp
public interface IMessageSubscriber
{
    void Subscribe<T>(string address, Func<T, Task> handler);
}
```

### `IMessageDispatcher` (The Core Router)
Injected into the Orchestrator and Runner business logic. It hides physical addresses by looking up the correct `IMessageTransport` and destination based on the message type.
```csharp
public interface IMessageDispatcher
{
    Task DispatchAsync<T>(T message);
    Task<TResponse> DispatchAndReceiveAsync<TRequest, TResponse>(TRequest message);
}
```

---

## 2. In-Process Message Transport Configuration

By default, the engine is bootstrapped under `Workflows.Hosting.InProcess` using loopback/in-memory messaging transports. 

### Bootstrapping in `InProcessHost.cs`
At startup, the host registers `InProcessMessageTransport` and `InProcessMessageSubscriber` to handle all traffic.

```csharp
// 1. Register transports
services.AddSingleton<InProcessMessageTransport>();
services.AddSingleton<InProcessMessageSubscriber>();

// 2. Build routing rules
var routingBuilder = new TransportRoutingBuilder();
routingBuilder.UseDefault<InProcessMessageTransport, InProcessMessageSubscriber>();

// Explicitly route execution requests to the loopback transport
routingBuilder.ForMessage<StartWorkflowRequest>()
    .Use<InProcessMessageTransport, InProcessMessageSubscriber>("in-process-runner");
routingBuilder.ForMessage<WorkflowExecutionRequest>()
    .Use<InProcessMessageTransport, InProcessMessageSubscriber>("in-process-runner");

services.AddSingleton(routingBuilder);
services.AddSingleton<ITransportFactory, DefaultTransportFactory>();
services.AddSingleton<IMessageDispatcher, DefaultMessageDispatcher>();
```

---

## 3. The In-Process Channels Execution Loop

When a workflow is started or a signal is received, the messaging flows as follows:

```
[External Request / Signal]
             │
             ▼
    [InboxOrchestrator]
             │ (Validates & Enqueues)
             ▼
   [WorkflowExecutionChannel] ◄─── (System.Threading.Channels)
             │
             ▼
      [RunnerWorker] (Hosted Service)
             │ (Lease / Route version)
             ▼
     [WorkflowRunner] (Stateless Tick)
             │
             ▼
[WorkflowRunResult / Wait DTOs]
             │
             ▼
 [CoordinatorCommitWorker] (Persists state to SQLite DB)
```

### 1. Request Dispatching (In-Memory Channel)
`RunnerWorker` listens to a high-performance in-memory channel (`WorkflowExecutionChannel`). 
When the client or orchestrator initiates an action, a `StartWorkflowRequest` or `WorkflowExecutionRequest` is pushed into the channel:

```csharp
public class WorkflowExecutionChannel
{
    private readonly Channel<WorkflowExecutionContext> _channel = Channel.CreateUnbounded<WorkflowExecutionContext>(...);
    public ChannelWriter<WorkflowExecutionContext> EgressWriter => _channel.Writer;
    public ChannelReader<WorkflowExecutionContext> IngressReader => _channel.Reader;
}
```

### 2. Processing (RunnerWorker)
The `RunnerWorker` background service loops asynchronously reading requests from the ingress reader, resolves a scoped DI container, determines the workflow version using `WorkflowVersionRouter`, and dispatches it to the stateless `WorkflowRunner`:

```csharp
while (await reader.WaitToReadAsync(stoppingToken))
{
    while (reader.TryRead(out var context))
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<WorkflowRunner>();
            // Route version and execute
            var result = await runner.RunWorkflowAsync(req);
            ...
        }
    }
}
```

### 3. State Preservation & Commit
Upon completing the state machine tick, the runner returns the updated run context and wait DTOs. The `CoordinatorCommitWorker` picks up the state delta and commits it atomically to the SQLite database (marking completed waits and inserting newly yielded ones).

---

## 4. Signal Ingestion

External signals do not use custom HTTP transport adapters or brokers. They are posted directly to the orchestrator interface (`IOrchestrator.PostSignalAsync`) via the API controllers or dashboard services:

```csharp
public interface IOrchestrator
{
    Task StartWorkflowAsync(StartWorkflowRequest request);
    Task PostSignalAsync(string signalPath, object signalPayload, Guid? signalId = null);
}
```

This ensures that signal handling is transactional, idempotent, and leverages the SQLite-backed inbox tables for deduplication prior to runner dispatch.