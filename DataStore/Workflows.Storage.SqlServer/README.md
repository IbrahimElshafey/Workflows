# Workflows.Storage.SqlServer

## 1. What is this?
The Microsoft SQL Server database provider adapter (targeting `net10.0`) for the workflow engine.

Key responsibilities:
- Configures `WorkflowsDbContext` to connect to SQL Server instances.
- Handles SQL Server-specific SQL dialects, migration generation, and concurrency exception handling.

## 2. How to use?
Reference this project in your host application and register it with the SQL Server connection string during startup.

### Example:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Workflows.Storage.SqlServer;

var services = new ServiceCollection();

// Configures the storage engine to use Microsoft SQL Server
services.AddWorkflowsSqlServer("Server=localhost;Database=WorkflowsDB;Trusted_Connection=True;TrustServerCertificate=True;");
```
