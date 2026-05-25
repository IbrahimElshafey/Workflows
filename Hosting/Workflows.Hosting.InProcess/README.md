# Workflows.Hosting.InProcess

## 1. What is this?
An in-process host bootstrapper (targeting `net10.0`) designed for single-process architectures (monoliths or lightweight worker nodes). It simplifies deployment by running the orchestrator, scheduler, in-memory runner, and SQLite database storage within the same application process.

Key features:
- Bundles SQLite database storage configurations.
- Runs the orchestrator background scheduler.
- Sets up loopback/in-memory message transports for internal communication.

## 2. How to use?
Call `AddWorkflowsInProcessHost` on your services configuration collection during application bootstrapping.

### Example:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Workflows.Hosting.InProcess;

var builder = WebApplication.CreateBuilder(args);

// Bootstraps orchestrator, scheduler, sqlite, and runner in-process
builder.Services.AddWorkflowsInProcessHost("Data Source=workflows.db");
```
