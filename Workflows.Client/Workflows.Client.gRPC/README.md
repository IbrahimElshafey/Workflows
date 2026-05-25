# Workflows.Client.gRPC

## 1. What is this?
A gRPC-based communication transport library (targeting `net10.0`) providing high-performance, strongly-typed RPC channels for communication between the workflow Orchestrator, runner runtimes, and worker/client nodes.

Key features:
- High-performance, streaming communication protocol support.
- gRPC transport implementation for signal propagation and command dispatching.
- Auto-generated proto contracts integration.

## 2. How to use?
Reference this package when you want to use gRPC instead of HTTP for inter-process communication in your cluster.

### Example (Server Startup):
```csharp
var builder = WebApplication.CreateBuilder(args);

// Register gRPC transport services
builder.Services.AddWorkflowsGrpcTransport();

var app = builder.Build();

// Map internal workflow gRPC services
app.MapWorkflowGrpcServices();
app.Run();
```
