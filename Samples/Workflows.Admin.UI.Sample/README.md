# Workflows.Admin.UI.Sample

A minimal ASP.NET Core host demonstrating how to embed the `Workflows.Admin.UI` module.

## Running the sample

```bash
dotnet run --project Samples/Workflows.Admin.UI.Sample
```

The application will start on:
- http://localhost:5000
- https://localhost:5001

The root URL redirects to `/admin/Dashboard`.

## Configuration

The sample uses SQLite by default (`workflows-admin-sample.db`). You can override the connection string via `appsettings.json` or environment variables:

```json
{
  "ConnectionStrings": {
    "Workflows": "Data Source=workflows-admin-sample.db"
  }
}
```

## What is shown

- **Dashboard** — instance counts, active waits, daily activity chart, alerts.
- **Instances** — filterable, paginated list of workflow instances.
- **Instance Detail** — JSON variable inspector, wait status tree, action panel.
- **Definitions** — workflow definitions grouped by name with version history.
- **Definition Detail** — visual topology DAG rendered with vis-network.
- **Execution Trace** — timeline of signals, commands, waits, and cancellations.

## Enabling write actions

Write actions (start, cancel, signal, terminate) are enabled automatically when `IOrchestrator` is registered in DI. This sample registers the full in-process workflow engine, so the action panel is available on instance detail pages.

To run the admin UI in read-only mode, omit `AddWorkflowsOrchestrator()` and `AddWorkflowsInProcessHosting()` from `Program.cs`.
