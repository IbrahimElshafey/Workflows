# Workflows.Storage.Sqlite

## 1. What is this?
The SQLite database provider adapter (targeting `net10.0`) for the workflow engine. This provider is highly recommended for local development, integration testing, and in-process hosting scenarios due to its zero-configuration dependency.

Key responsibilities:
- Configures `WorkflowsDbContext` to connect to SQLite databases.
- Sets up database files or in-memory SQLite providers for test isolation.

## 2. How to use?
Reference this project in your host or test project, and register it with the database connection details.

### Example:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Workflows.Storage.Sqlite;

var services = new ServiceCollection();

// Configures the storage engine to use SQLite
services.AddWorkflowsSqlite("Data Source=workflows.db");
```
