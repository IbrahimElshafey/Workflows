# Workflows.Admin.UI

An embedded ASP.NET Core MVC administration module for the Workflows engine.

## Features

- **Dashboard** — instance counts, active waits, daily activity chart, alerts.
- **Instance Explorer** — filterable, paginated list with detail view.
- **Instance Detail** — JSON variable inspector, wait status tree, cancellation history, action panel.
- **Definitions Explorer** — workflow definitions grouped by name/version.
- **Topology Viewer** — visual DAG rendered with vis-network.
- **Execution Trace** — unified timeline of signals, commands, waits, and cancellations.
- **Instance Control Panel** — dispatch signals, trigger cancellation tokens, terminate instances (requires `IOrchestrator`).

## Installation

Add the project reference to your ASP.NET Core host:

```xml
<ProjectReference Include="..\Workflows.Admin.UI\Workflows.Admin.UI.csproj" />
```

## Usage

In `Program.cs`:

```csharp
using Workflows.Admin.UI.Extensions;
using Workflows.Admin.UI;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Workflows");

builder.Services.AddWorkflowsAdminUI(connectionString, options =>
{
    options.RoutePrefix = "admin";
    options.Provider = AdminDbProvider.Sqlite;
    options.EnableWriteActions = true;
});

var app = builder.Build();

app.UseRouting();
app.UseWorkflowsAdminUI();

app.Run();
```

## Configuration

| Option | Description | Default |
|--------|-------------|---------|
| `RoutePrefix` | URL prefix for the admin UI | `admin` |
| `PageSize` | Default page size for lists | `25` |
| `MaxPageSize` | Maximum allowed page size | `250` |
| `EnableWriteActions` | Expose start/cancel/signal/terminate actions | `true` |
| `Provider` | Database provider (`Sqlite`, `SqlServer`, `Postgres`) | `Sqlite` |

## Write actions

The admin UI can operate in read-only mode. Write actions are only available when an `IOrchestrator` implementation is registered in the DI container. If `EnableWriteActions` is `true` but no orchestrator is present, the action panel shows an informational message instead of the forms.

## Views

All views, controllers, and static assets are embedded in the `Workflows.Admin.UI` assembly and served automatically via `AddWorkflowsAdminUI` / `UseWorkflowsAdminUI`.

## External libraries

The UI uses CDN-hosted libraries (no npm/build step required):

- Bootstrap 5.3.2
- Bootstrap Icons 1.11.1
- Chart.js 4.4.1
- vis-network

## Sample

See `Samples/Workflows.Admin.UI.Sample` for a complete working host.
