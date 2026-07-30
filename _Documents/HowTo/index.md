# Workflows Usage Guides Index

Welcome to the Workflows developer guide index. This central hub outlines how to author, host, configure, administer, and version workflows within this repository.

---

## 🗺️ Learning Path & Key Topics

### 1. Introduction & Architecture
* **[Introduction & Wiki Home](file:///d:/MySrc/Workflows/Workflows.wiki/Home.md):** High-level overview of the workflow engine's goals, topology, and core components.
* **[Architecture Overview](file:///d:/MySrc/Workflows/Workflows.wiki/Architecture-Overview.md):** Transaction outbox, split database architecture, and execution channel pipeline.
* **[Architectural Reference Guide](file:///d:/MySrc/Workflows/_Documents/Architecture/Architectural%20Reference%20Guide.md):** Architectural reference for developers working on runtime execution internals.
* **[Project Dependency Graph](file:///d:/MySrc/Workflows/_Documents/ProjectDependencies.md):** Complete solution project dependency diagram.

### 2. Defining Workflows
* **[Writing Workflows (DSL Guide)](file:///d:/MySrc/Workflows/Workflows.wiki/Writing-Workflows.md):** Step-by-step instructions on creating a `WorkflowContainer`, declaring state properties, and writing execution blocks.
* **[Sub-Workflow Execution](file:///d:/MySrc/Workflows/_Documents/Design_Patterns/Sub-Workflow%20Execution.md):** Guide on nesting workflows within parent containers and managing their lifecycle/state.
* **[Workflow Compensation Logic (Saga Pattern)](file:///d:/MySrc/Workflows/_Documents/Design_Patterns/Workflow%20Compensation%20Logic%20(Saga%20Pattern).md):** How to define rollback blocks and compensation paths for failure recovery.
* **[Roslyn Code Analyzers Guide](file:///d:/MySrc/Workflows/Workflows.Analyzers/README.md):** Build-time analyzer rules enforcing `sealed` containers, no variable closures, and no unsafe references.

### 3. Hosting, Administration & Command Execution
* **[In-Process Hosting Guide](file:///d:/MySrc/Workflows/Workflows.wiki/In-Process-Hosting.md):** How to host the workflow engine in-process inside your application, configure DI, and start runner workers.
* **[Embedded Admin Web UI Guide](file:///d:/MySrc/Workflows/Workflows.Admin.UI/README.md):** Embed the ASP.NET Core MVC Admin Dashboard, visualize DAG topologies (vis-network), and inspect traces.
* **[Command Execution Modes](file:///d:/MySrc/Workflows/_Documents/Architecture/Command%20Execution%20Modes.md):** Understand the difference between Immediate, Standard Async, and Deferred Async command dispatching.

### 4. CLI Tooling, Versioning & State Migrations
* **[CLI Tooling (`dotnet-wf`) Guide](file:///d:/MySrc/Workflows/Workflows.Tools.CLI/README.md):** Command reference for schema extraction, pre-publish verification gates, impact manifests, and C# migration boilerplate.
* **[Out-of-Process Worker Supervisor Architecture](file:///d:/MySrc/Workflows/_Documents/Roadmap/Versioning/Out-of-Process%20Worker%20Supervisor%20Architecture.md):** Multi-version SxS process isolation and Named Pipe IPC protocol mechanics.
* **[Side-by-Side (SxS) Execution Mechanics](file:///d:/MySrc/Workflows/_Documents/Roadmap/Versioning/How%20SxS%20(Side-by-Side)%20Execution%20Works.md):** How the engine compiles and executes old workflow versions in isolated dynamic AssemblyLoadContexts.
* **[Migrating Workflows By Sample](file:///d:/MySrc/Workflows/_Documents/HowTo/Migrating_Workflows_By_Sample.md):** Tutorial on version increments, Roslyn code-fix archiving, and implementing `WorkflowMigration<TOld, TNew>` classes.
* **[SxS vs. Migration Strategies](file:///d:/MySrc/Workflows/_Documents/Roadmap/Versioning/Versioning%20-%20Why%20Both%20SxS%20and%20Migration.md):** Decision framework on when to let old instances run under SxS vs. when to write an active data migration.
