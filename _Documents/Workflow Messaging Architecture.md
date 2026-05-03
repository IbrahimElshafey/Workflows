# Workflow Messaging Architecture

This document outlines the communication architecture between the **Workflow Orchestrator** and the **Workflow Runners**. 

The core philosophy of this design is **100% Infrastructure Agnosticism**. The domain logic never interacts with magic strings, HTTP URLs, or RabbitMQ queue names. Instead, it relies on a strongly-typed, expression-based routing engine that maps logical .NET types to physical transport mechanisms at application startup.

---

## 1. The Core Interfaces

The communication layer is built on three primary interfaces, completely firewalling the domain from the underlying transport (HTTP, RabbitMQ, Kafka, etc.).

### `IMessageTransport` (The Sender Engine)
Implemented by physical transports (e.g., `RabbitMqTransport`, `HttpTransport`). It requires a physical destination address.
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

## 2. Smart Routing via Expression Trees

To prevent hardcoding queues or URLs in the execution logic, we use the `TransportRoutingBuilder`. This compiles Expression Trees at startup to evaluate high-performance routing rules.

**Configuration Example (`Startup.cs`):**
```csharp
var builder = new TransportRoutingBuilder();

// Route Request-Response (RPC) over HTTP
builder.ForMessage<BulkRegistrationPackage>()
       .Use<HttpTransport, HttpSubscriber>("http://orchestrator-api/register");

// Route Fire-and-Forget execution over RabbitMQ based on workflow type
builder.ForMessage<WorkflowExecutionRequest>()
       .When(req => req.Context.WorkflowTypeName == "OrderWorkflow")
       .Use<RabbitMqTransport, RabbitMqSubscriber>("orders-execution-queue");
```

---

## 3. Execution Patterns

### A. Fire-and-Forget (Workflow Execution)
Used when the Orchestrator commands a Runner to execute, or when a Runner pushes a result back. Threads are immediately freed.

*   **Orchestrator:** `await _dispatcher.DispatchAsync(executionRequest);`
*   **Behind the scenes:** Evaluates rule -> Resolves `RabbitMqTransport` -> Publishes to `orders-execution-queue`.

### B. Request-Response (Registration Sync)
Used for fast operations requiring immediate feedback, utilizing RPC patterns (e.g., temporary reply queues in RabbitMQ).

*   **Runner:** `await _dispatcher.DispatchAndReceiveAsync<BulkRegistrationPackage, SyncResult>(package);`
*   **Behind the scenes:** Pauses thread -> Waits for Orchestrator to confirm SQL save -> Resumes.

---

## 4. Signal Ingestion (The ASP.NET Bridge)

When external systems send Signals (e.g., "Payment Approved") via standard API calls, we use a streamlined bridge to maintain our agnostic core while leveraging standard ASP.NET routing.

### The Bridge Subscriber
This holds the handler logic in memory, waiting for the ASP.NET Controller to pass the data.
```csharp
public class HttpSubscriber : IMessageSubscriber
{
    private readonly ConcurrentDictionary<Type, Func<object, Task>> _handlers = new();

    // The address string is ignored; ASP.NET handles routing
    public void Subscribe<T>(string address, Func<T, Task> handler)
    {
        _handlers[typeof(T)] = async (obj) => await handler((T)obj);
    }

    public async Task TriggerAsync<T>(T message)
    {
        if (_handlers.TryGetValue(typeof(T), out var handler))
        {
            await handler(message);
        }
        else
        {
            throw new InvalidOperationException($"No subscriber registered for type {typeof(T).Name}");
        }
    }
}
```

### The Standard Controller
A clean, secure entry point for external systems.

```csharp
[ApiController]
[Route("api/signals")]
public class SignalsController : ControllerBase
{
    private readonly HttpSubscriber _subscriber;

    public SignalsController(HttpSubscriber subscriber)
    {
        _subscriber = subscriber;
    }

    [HttpPost("receive")]
    public async Task<IActionResult> ReceiveSignal([FromBody] SignalRequest request)
    {
        try
        {
            // Pass the typed request directly into the agnostic abstraction
            await _subscriber.TriggerAsync(request);
            return Accepted(new { Message = "Signal accepted." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }
}
```

### The Bootstrapper
A background service that wires the core `ISignalProcessor` to the HTTP Bridge at startup.

```csharp
public class OrchestratorBootstrapper : IHostedService
{
    private readonly ITransportFactory _factory;
    private readonly ISignalProcessor _signalProcessor;

    public OrchestratorBootstrapper(ITransportFactory factory, ISignalProcessor signalProcessor)
    {
        _factory = factory;
        _signalProcessor = signalProcessor;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Resolve the HttpSubscriber registered for this type
        var subscriber = _factory.GetSubscriber<SignalRequest>();

        // Bind the core processing logic to the bridge
        subscriber.Subscribe<SignalRequest>(
            "ignored-for-http", 
            async (signal) => await _signalProcessor.ProcessSignalAsync(signal)
        );

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

````