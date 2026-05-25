# Workflows.Communication.Abstraction

## 1. What is this?
A transport-agnostic routing framework (targeting `netstandard2.1`) for managing communication between the Orchestrator, runner processes, and clients. 

Key abstractions:
- `IMessageTransport` & `IMessageSubscriber`: Interfaces for implementing custom network transport layers (e.g., HTTP, gRPC, RabbitMQ).
- `IMessageDispatcher`: Resolves and routes messages dynamically based on type registrations.
- `TransportRoutingBuilder`: Fluent API to configure message-to-transport routing rules at startup.

## 2. How to use?
Use this project to configure routing mappings when bootstrapping the orchestrator or worker hosts.

### Example:
```csharp
using Workflows.Communication.Abstraction;

var routing = new TransportRoutingBuilder();

// Route StartWorkflowRequest messages over Http to a specific endpoint
routing.ForMessage<StartWorkflowRequest>()
       .Use<HttpWorkflowMessageTransport, HttpWorkflowMessageSubscriber>("http://orchestrator-api/start");
```
