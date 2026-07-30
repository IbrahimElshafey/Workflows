Welcome to the **Workflows Engine** wiki!

The Workflows Engine is a high-performance, lightweight, snapshot-serialized workflow engine for .NET 10. Rather than using event-sourcing and expensive event replay (like Temporal or Durable Functions) to rebuild execution state, this engine directly serializes compiler-generated C# state machine states and container properties into JSON. This results in sub-millisecond execution ticks and instant resumption.

---

## 📖 Wiki Navigation

Use the sidebar or the links below to navigate the documentation:

* **[Architecture Overview](Architecture-Overview)**: Hybrid database design, execution cycle, stateless compute, out-of-process IPC workers, and Admin UI architecture.
* **[Writing Workflows](Writing-Workflows)**: Developer guide to authoring workflows, wait primitives, sub-workflows, Roslyn static analysis rules, and no-closure constraints.
* **[In-Process & Worker Hosting](In-Process-Hosting)**: Set up unified in-process hosts (SQLite/Postgres/SQL Server), WebAPI/gRPC endpoints, and out-of-process worker supervisors.
* **[CLI Tooling Suite (`dotnet-wf`)](file:///d:/MySrc/Workflows/Workflows.Tools.CLI/README.md)**: Command reference for schema extraction, build pipeline drift verification, manifest generation, and migration boilerplate.
* **[Embedded Admin Web UI](file:///d:/MySrc/Workflows/Workflows.Admin.UI/README.md)**: Explore the ASP.NET Core dashboard, vis-network DAG visualization, execution trace timelines, and live signal triggers.

---

## 🚀 Quick Start & Demos

To see the engine in action immediately:

1. **Prerequisites**: Ensure you have the [.NET 10.0 SDK](https://dotnet.microsoft.com/download) installed.
2. **Run the SQLite Interactive Console CLI Demo**:
   ```bash
   dotnet run --project Samples/InProcessSqliteSample/InProcessSqliteSample.csproj
   ```
3. **Run the ASP.NET Core Admin Web UI Sample**:
   ```bash
   dotnet run --project Samples/Workflows.Admin.UI.Sample/Workflows.Admin.UI.Sample.csproj
   ```
   Open `http://localhost:5000/admin` in your browser to inspect instance metrics, visual DAGs, and execution traces.
4. **Run the Out-of-Process Worker Process Supervisor Demo**:
   ```bash
   dotnet run --project Samples/OutOfProcessWorkerSample/OutOfProcessWorkerSample.csproj
   ```
   Demonstrates launching worker sub-processes over Named Pipe IPC and generating `dotnet-wf` contract artifacts.
