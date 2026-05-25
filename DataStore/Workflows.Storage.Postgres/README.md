# Workflows.Storage.Postgres

## 1. What is this?
The PostgreSQL database provider adapter (targeting `net10.0`) for the workflow engine.

Key responsibilities:
- Configures `WorkflowsDbContext` to connect to PostgreSQL database engines.
- Utilizes PostgreSQL-specific features (such as native JSONB columns via `Npgsql.EntityFrameworkCore.PostgreSQL`) for highly efficient query and index execution on serialized workflow execution data.

## 2. How to use?
Reference this project in your host application and register it with the PostgreSQL connection string during startup.

### Example:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Workflows.Storage.Postgres;

var services = new ServiceCollection();

// Configures the storage engine to use PostgreSQL
services.AddWorkflowsPostgres("Host=localhost;Database=workflows;Username=postgres;Password=mysecretpassword");
```
