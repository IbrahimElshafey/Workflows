# 🏗️ Workflows Engine: Project Summary

A high-performance, lightweight, snapshot-serialized workflow engine for **.NET 10**.

Unlike traditional workflow engines (such as Temporal or Durable Functions) that rely on event-sourcing and expensive replay mechanisms to rebuild execution state, this engine directly serializes compiler-generated C# state machine states and container properties into JSON. This results in sub-millisecond execution ticks, minimal database overhead, and instant workflow resumption.

---

## 🏛️ Architectural Pillars

The engine is built on a strict separation of **Domain Definition**, **I/O & Persistence**, **Compute & Execution**, **Client Interfaces**, and **Tooling/Administration**.

```mermaid
graph TD
    subgraph Client & Administration
        Client[Workflows.Client / gRPC / WebApi]
        AdminUI[Workflows.Admin.UI]
        CLI[Workflows.Tools.CLI / dotnet-wf]
    end

    subgraph Hosting & Control Plane
        Host[Workflows.Hosting.InProcess]
        Supervisor[WorkerProcessSupervisor]
        Orch[Workflows.Orchestrator]
    end

    subgraph I/O & Storage Layer
        DB[(RDBMS: Sqlite / Postgres / SqlServer)]
        EFCore[Workflows.Storage.EntityFrameworkCore]
    end

    subgraph Compute & Execution (Stateless)
        Runner[Workflows.Runner]
        Defs[Workflows.Definition]
        Analyzers[Workflows.Analyzers Roslyn]
    end

    Client -- Signals/Commands --> Orch
    AdminUI -- Visual Monitoring / Signals --> Orch
    Orch -- In-Memory Channel / Named Pipe IPC --> Runner
    Orch -- Commit SQL Index & JSON Blob --> EFCore
    EFCore --> DB
    Supervisor -- Spawns Isolated ALC Processes --> Runner
    Analyzers -- Build-Time Rules --> Defs
```

### 1. [Workflows.Definition](file:///d:/MySrc/Workflows/Workflows.Definition/Workflows.Definition.csproj) (DSL Layer)
* **Responsibility**: Defines a zero-dependency, fluent Domain Specific Language (DSL) for authoring workflows.
* **Key Concept**: Developers inherit from `WorkflowContainer` and return `IAsyncEnumerable<Wait>` using native C# `yield return` statements for control flow.
* **Wait Primitives**: Provides core suspension nodes: `WaitSignal`, `WaitDelay` (Timers), `WaitGroup` (Parallel branching/MatchAny/MatchAll), `WaitSubWorkflow` (Recursive child execution), and Saga compensations.
* **Clean Abstraction**: Infrastructure-specific DTOs are kept `internal` and shared with the Runner using `[InternalsVisibleTo]`, keeping the author's IDE workspace clean.

### 2. [Workflows.Orchestrator](file:///d:/MySrc/Workflows/Workflows.Orchestrator/Workflows.Orchestrator.csproj) (I/O & Persistence)
* **Responsibility**: Central transaction coordinator and signal router. It maps incoming signals to instances.
* **Persistence Strategy**: Implements a **Hybrid Document-Relational Model** to balance execution speed and query performance.
* **State Hydration**: When a signal arrives, the Orchestrator performs a quick SQL index lookup to locate the instance, loads and deserializes its JSON state, and dispatches it to the stateless Runner.

### 3. [Workflows.Runner](file:///d:/MySrc/Workflows/Workflows.Runner/Workflows.Runner.csproj) (Compute & Execution)
* **Responsibility**: The stateless "brain" of the engine. It handles compiler state machine progression, C# expression tree parsing, and delegate evaluation.
* **Stateless execution**: It does not touch databases or I/O. It consumes execution requests, runs in-memory ticks, and returns updated context snapshots and wait DTOs.
* **Optimized Hot Paths**: Memory-caches compiled delegates to avoid reflection costs on repeat runs.

### 4. [Workflows.Abstraction](file:///d:/MySrc/Workflows/Workflows.Abstraction/Workflows.Abstraction.csproj) & [Workflows.Communication.Abstraction](file:///d:/MySrc/Workflows/Workflows.Communication.Abstraction/Workflows.Communication.Abstraction.csproj)
* **Responsibility**: Decoupling boundaries of the engine. Defines transports (`IMessageTransport`, `IMessageSubscriber`, `IMessageDispatcher`) and routes execution requests via loopback (`InProcessMessageTransport`), Named Pipe IPC (`WorkerProcessSupervisor`), or gRPC/HTTP transports.

### 5. [Workflows.Admin.UI](file:///d:/MySrc/Workflows/Workflows.Admin.UI/Workflows.Admin.UI.csproj) (Administration & Dashboard)
* **Responsibility**: Embedded ASP.NET Core MVC administration dashboard module.
* **Key Features**: Visual DAG topology rendering (`vis-network`), step execution trace timeline, JSON variable state inspector, and live signal/cancellation control panel.

### 6. [Workflows.Tools.CLI](file:///d:/MySrc/Workflows/Workflows.Tools.CLI/Workflows.Tools.CLI.csproj) & [Workflows.Analyzers](file:///d:/MySrc/Workflows/Workflows.Analyzers/Workflows.Analyzers.csproj) (Tooling & Static Analysis)
* **Responsibility**: `dotnet-wf` command-line utility and Roslyn build-time code analyzer.
* **CLI Capabilities**: Offline contract schema JSON export, CI/CD schema drift verification gate, pre-publish deployment manifests, and strongly-typed C# `WorkflowMigration` class generation.
* **Roslyn Checks**: Enforces `sealed` containers, prevents variable closure captures, and blocks unsafe runtime dependencies.

---

## 💾 The Storage Layer (Hybrid Document-Relational)

To achieve sub-millisecond routing and avoid full-table scans of JSON blobs, the persistence layer separates the **Source of Truth** from the **Routing Indexes**.

### 1. The Aggregate Root (`WorkflowInstances` Table)
Stores the entire execution context in a single, versioned database row containing a serialized JSON/JSONB blob.
* `Id` (Guid, PK)
* `Status` (Enum: `Running`, `Completed`, `InError`)
* `StateObject` (JSON/JSONB): Serialized `WorkflowRunContext` containing local variables, state machine step index, and wait tree.

### 2. The Indexing Tables
Flat relational tables index active waits to support fast, direct lookups when signals or callbacks arrive.

| Index Table | Purpose | Keys |
| :--- | :--- | :--- |
| `SignalWaits` | Indexes pending signals and delay timers | `WorkflowInstanceId`, `SignalPath` (Indexed) |
| `CommandWaits` | Tracks pending outgoing asynchronous commands | `WorkflowInstanceId`, `CommandWaitId` |
| `CompensationWaits` | Indexes Saga undo/rollback operations | `WorkflowInstanceId`, `CompensationWaitId` |

---

## 📂 Codebase Directory Map (All 26 Projects)

| Directory / Project | Layer | Purpose |
| :--- | :--- | :--- |
| **[`Workflows.sln`](file:///d:/MySrc/Workflows/Workflows.sln)** | Root Solution | Houses all code modules, tools, and sample applications. |
| **[`Workflows.Primitives/`](file:///d:/MySrc/Workflows/Workflows.Primitives/Workflows.Primitives.csproj)** | Primitives | Primitive state structures and wait definitions. |
| **[`Workflows.Abstraction/`](file:///d:/MySrc/Workflows/Workflows.Abstraction/Workflows.Abstraction.csproj)** | Interfaces | Interfaces for orchestrator, runner, and storage repositories. |
| **[`Workflows.Communication.Abstraction/`](file:///d:/MySrc/Workflows/Workflows.Communication.Abstraction/Workflows.Communication.Abstraction.csproj)** | Messaging | Interface decoupling client, runner, and orchestrator boundaries. |
| **[`Workflows.Definition/`](file:///d:/MySrc/Workflows/Workflows.Definition/Workflows.Definition.csproj)** | DSL | Declarative primitives, fluent builders, and scan registration. |
| **[`Workflows.Common.Abstraction/`](file:///d:/MySrc/Workflows/Workflows.Common.Abstraction/Workflows.Common.Abstraction.csproj)** | Shared Contracts | Shared serialization, JSON, and expression conversion interfaces. |
| **[`Workflows.Common/`](file:///d:/MySrc/Workflows/Workflows.Common/Workflows.Shared.csproj)** | Shared Impl | Nuqleon Bonsai expression serializers, System.Text.Json, DI helpers. |
| **[`Workflows.Runner/`](file:///d:/MySrc/Workflows/Workflows.Runner/Workflows.Runner.csproj)** | Compute | Stateful compiler wrapper, state advancer, and expression matchers. |
| **[`Workflows.Orchestrator/`](file:///d:/MySrc/Workflows/Workflows.Orchestrator/Workflows.Orchestrator.csproj)** | Persistence | Coordinate transactions, query indexes, trigger timers, schedule tasks. |
| **[`Hosting/Workflows.Hosting.InProcess/`](file:///d:/MySrc/Workflows/Hosting/Workflows.Hosting.InProcess/Workflows.Hosting.InProcess.csproj)** | Host | In-memory runner client, Named Pipe IPC worker supervisor, SQLite setup. |
| **[`Workflows.Client/Workflows.Client/`](file:///d:/MySrc/Workflows/Workflows.Client/Workflows.Client/Workflows.Client.csproj)** | Client SDK | Client API for posting signals and background command execution. |
| **[`Workflows.Client/Workflows.Client.gRPC/`](file:///d:/MySrc/Workflows/Workflows.Client/Workflows.Client.gRPC/Workflows.Client.gRPC.csproj)** | Client Transport | gRPC streaming transport for distributed orchestrators and workers. |
| **[`Workflows.Client/Workflows.Client.WebApi/`](file:///d:/MySrc/Workflows/Workflows.Client/Workflows.Client.WebApi/Workflows.Client.WebApi.csproj)** | Client Transport | HTTP WebAPI endpoints for orchestrator/worker interaction. |
| **[`DataStore/Workflows.Storage.EntityFrameworkCore/`](file:///d:/MySrc/Workflows/DataStore/Workflows.Storage.EntityFrameworkCore/Workflows.Storage.EntityFrameworkCore.csproj)** | Storage Base | `WorkflowsDbContext` entity mappings and TPH table configurations. |
| **[`DataStore/Workflows.Storage.Sqlite/`](file:///d:/MySrc/Workflows/DataStore/Workflows.Storage.Sqlite/Workflows.Storage.Sqlite.csproj)** | Storage Provider | SQLite dialect and EF Core mappings. |
| **[`DataStore/Workflows.Storage.Postgres/`](file:///d:/MySrc/Workflows/DataStore/Workflows.Storage.Postgres/Workflows.Storage.Postgres.csproj)** | Storage Provider | Postgres dialect with JSONB column optimization. |
| **[`DataStore/Workflows.Storage.SqlServer/`](file:///d:/MySrc/Workflows/DataStore/Workflows.Storage.SqlServer/Workflows.Storage.SqlServer.csproj)** | Storage Provider | Microsoft SQL Server dialect and EF Core mappings. |
| **[`Workflows.Admin.UI/`](file:///d:/MySrc/Workflows/Workflows.Admin.UI/Workflows.Admin.UI.csproj)** | Admin UI | Embedded ASP.NET Core MVC dashboard & vis-network DAG renderer. |
| **[`Workflows.Tools.CLI/`](file:///d:/MySrc/Workflows/Workflows.Tools.CLI/Workflows.Tools.CLI.csproj)** | CLI Tools | `dotnet-wf` tool for schema extraction, verification gate, and migrations. |
| **[`Workflows.Analyzers/`](file:///d:/MySrc/Workflows/Workflows.Analyzers/Workflows.Analyzers.csproj)** | Analyzers | Roslyn code analyzer checking scope closures and container rules. |
| **[`Samples/InProcessSqliteSample/`](file:///d:/MySrc/Workflows/Samples/InProcessSqliteSample/InProcessSqliteSample.csproj)** | Sample | Interactive CLI database monitoring dashboard. |
| **[`Samples/Workflows.Admin.UI.Sample/`](file:///d:/MySrc/Workflows/Samples/Workflows.Admin.UI.Sample/Workflows.Admin.UI.Sample.csproj)** | Sample | Embedded ASP.NET Core Admin Web UI host. |
| **[`Samples/OutOfProcessWorkerSample/`](file:///d:/MySrc/Workflows/Samples/OutOfProcessWorkerSample/OutOfProcessWorkerSample.csproj)** | Sample | Named Pipe IPC out-of-process worker process supervisor & CLI demo. |
| **[`Samples/WorkflowSample/`](file:///d:/MySrc/Workflows/Samples/WorkflowSample/WorkflowSample.csproj)** | Sample | CLI sandbox with example workflow templates. |
| **[`Tests/Workflows.Runner.Tests/`](file:///d:/MySrc/Workflows/Tests/Workflows.Runner.Tests/Workflows.Runner.Tests.csproj)** | Test Suite | Primary unit and integration test project. |
| **[`Workflows.Runner.TestShell/`](file:///d:/MySrc/Workflows/Workflows.Runner.TestShell/Workflows.Runner.TestShell.csproj)** | Test Shell | Helper shell harness for runner verification tests. |
| **[`Tests/TestSomething/`](file:///d:/MySrc/Workflows/Tests/TestSomething/TestSomething.csproj)** | Test Host | Minimal test scratch host. |
