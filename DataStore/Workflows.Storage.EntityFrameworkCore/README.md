# Workflows.Storage.EntityFrameworkCore

## 1. What is this?
The core Entity Framework Core database mapping library (targeting `net10.0`) for the workflow engine. It contains the primary DB contexts, entities, configurations, and repository implementations.

Key responsibilities:
- **`WorkflowsDbContext`**: The primary database context containing DbSets for workflows, waits, and logs.
- **TPH (Table-Per-Hierarchy) Mapping**: Configures inheritance hierarchies for various wait types (e.g., `SignalWait`, `CommandWait`, `DelayWait`) to store them efficiently in flat database tables.
- **Value Conversions & Auditing**: Handles custom converters (like converting dates or serializing complex JSON state payloads into database text columns) and interceptors for automatic auditing/soft deletes.

## 2. How to use?
Reference this project alongside a database-specific provider adapter (like SqlServer, Postgres, or Sqlite) in your startup host application.

### Example:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Workflows.Storage.EntityFrameworkCore;

var services = new ServiceCollection();

// Register the core EF Core storage engine
services.AddWorkflowsEntityFrameworkCore();
```
