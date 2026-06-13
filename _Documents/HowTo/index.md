# Workflows Usage Guides Index

Welcome to the Workflows developer guide index. This central hub outlines how to author, host, configure, and version workflows within this repository.

---

## 🗺️ Learning Path & Key Topics

### 1. Introduction & Architecture
* **[Introduction & Wiki Home](file:///d:/MySrc/Workflows/Workflows.wiki/Home.md):** Get a high-level overview of the workflow engine's goals, topology, and core components.
* **[Architecture Overview](file:///d:/MySrc/Workflows/Workflows.wiki/Architecture-Overview.md):** Learn about the transaction outbox, the split database architecture, and execution channel pipeline.
* **[Architectural Reference Guide](file:///d:/MySrc/Workflows/_Documents/Architecture/Architectural%20Reference%20Guide.md):** A detailed architectural reference for developers working on runtime execution internals.

### 2. Defining Workflows
* **[Writing Workflows (DSL Guide)](file:///d:/MySrc/Workflows/Workflows.wiki/Writing-Workflows.md):** Step-by-step instructions on creating a `WorkflowContainer`, declaring state properties, and writing execution blocks.
* **[Sub-Workflow Execution](file:///d:/MySrc/Workflows/_Documents/Design_Patterns/Sub-Workflow%20Execution.md):** Guide on nesting workflows within parent containers and managing their lifecycle/state.
* **[Workflow Compensation Logic (Saga Pattern)](file:///d:/MySrc/Workflows/_Documents/Design_Patterns/Workflow%20Compensation%20Logic%20(Saga%20Pattern).md):** How to define rollback blocks and compensation paths for failure recovery.

### 3. Hosting & Command Execution
* **[In-Process Hosting Guide](file:///d:/MySrc/Workflows/Workflows.wiki/In-Process-Hosting.md):** How to host the workflow engine in-process inside your application, configure DI, and start runner workers.
* **[Command Execution Modes](file:///d:/MySrc/Workflows/_Documents/Architecture/Command%20Execution%20Modes.md):** Understand the difference between Sync, Standard Async, and External Async command dispatching.

### 4. Versioning & State Migrations
* **[Side-by-Side (SxS) Execution Mechanics](file:///d:/MySrc/Workflows/_Documents/Roadmap/Versioning/How%20SxS%20(Side-by-Side)%20Execution%20Works.md):** How the engine compiles and executes old workflow versions in isolated collectible assembly load contexts.
* **[Migrating Workflows By Sample](file:///d:/MySrc/Workflows/_Documents/HowTo/Migrating_Workflows_By_Sample.md):** Practical tutorial on how to bump versions, use the code fix archiving tool, and implement `WorkflowMigration<TOld, TNew>` classes.
* **[SxS vs. Migration Strategies](file:///d:/MySrc/Workflows/_Documents/Roadmap/Versioning/Versioning%20-%20Why%20Both%20SxS%20and%20Migration.md):** Decision framework on when to let old instances run under SxS vs. when to write an active data migration.
