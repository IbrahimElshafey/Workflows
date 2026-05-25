# Workflows.Client.WebApi

## 1. What is this?
An HTTP Web API integration library (targeting `net10.0`) that implements communication between orchestrators and clients over HTTP.

Key features:
- **Server Endpoints**: Exposes endpoints on the orchestrator host (such as `/api/workflows/start`, `/api/workflows/signal`, and `/api/workflows/command-result`).
- **HTTP Transport**: Provides `HttpWorkflowMessageTransport` which handles posting signals, execution results, and workflow starts over HTTP.

## 2. How to use?
In corporate web API hosts (orchestrators or workers), configure endpoint mappings.

### Example (Server Startup):
```csharp
var builder = WebApplication.CreateBuilder(args);
// Add Orchestrator services...

var app = builder.Build();

// Exposes API endpoints for start, signal, etc.
app.MapWorkflowOrchestratorEndpoints();
app.Run();
```

### Example (Worker Startup):
```csharp
var builder = WebApplication.CreateBuilder(args);
// Add Client/Worker services...

var app = builder.Build();

// Exposes API endpoints for receiving command requests
app.MapWorkflowClientEndpoints();
app.Run();
```
