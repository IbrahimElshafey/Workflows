# Workflow Versioning: Implementation Task List

This document breaks down the implementation of the workflow versioning architecture into actionable phases for the engineering team. 

---

## Phase 1: Compiler Tooling (Roslyn Source Generator & Analyzers)
**Goal:** Automate the extraction of workflow schemas and the archiving of old versions directly within the IDE, eliminating manual CLI steps.

- [ ] **Task 1.1: `WorkflowVersionAttribute` Definition**
  - Create the `[WorkflowVersion("x.y.z")]` attribute that developers apply to `WorkflowContainer` implementations.
- [ ] **Task 1.2: Version Increment Analyzer & Code Fix Provider**
  - Build a Roslyn Analyzer that detects when a `[WorkflowVersion]` string is changed compared to the latest known schema.
  - Build a Code Fix Provider (the "lightbulb" action) that:
    - Moves the current `.cs` file to `Workflows/Archive/{WorkflowName}/V{xx}/`.
    - Modifies the `.csproj` to set `Build Action = None` for the archived file.
- [ ] **Task 1.3: CFG & Schema Generator (`.json`)**
  - Build an incremental Source Generator that parses a workflow's semantic model and AST.
  - Extract all `yield return` checkpoints, branching logic (`if/else`, loops), and class properties.
  - Serialize this data into `WorkflowName_Vxx_Schema.json` inside the archive folder.
- [ ] **Task 1.4: Layout Wrapper Generator (`.cs`)**
  - Extend the Source Generator to read `WorkflowName_Vxx_Schema.json`.
  - Dynamically emit `WorkflowNameVxx_Layout.cs` (e.g., `OrderWorkflowV1_Layout : WorkflowStateWrapper<OrderWorkflowV1Instance>`).
  - Ensure these emitted types are available for developers to write migration classes against.
- [ ] **Task 1.5: Continuous Schema Validation Analyzer**
  - Build an Analyzer that continuously checks archived workflow files (even those with `Build Action = None`) against their `_Schema.json`.
  - Emit fatal compiler diagnostics (e.g., `WF3002`) if structural drift or property type mismatches are detected.

---

## Phase 2: The Migration API (`MigrationContainer`)
**Goal:** Provide developers with an elegant, strongly-typed API to write explicit data migrations between workflow versions using the native workflow DSL.

- [ ] **Task 2.1: Implement `MigrationContainer` Base**
  - Create the abstract `MigrationContainer` class.
  - Implement stub factory methods mirroring `WorkflowContainer`: `WaitSignal<T>`, `WaitDelay`, `WaitGroup`, `WaitSubWorkflow`.
  - Implement migration-specific helpers: `RecreateWait`, `SubWorkflow_RecreateWait`, and `ScheduleCommand`.
- [ ] **Task 2.2: Implement `WorkflowMigration<TOld, TNew>`**
  - Create the generic base class inheriting from `MigrationContainer`.
  - Define the abstract methods: `MigrateInstance(TOld, TNew)` and `MigrateActiveWait(oldWait, _new)`.
  - Define the virtual method: `MigrateSubWorkflowState(oldSubWait, _new)`.
- [ ] **Task 2.3: Reflection Auto-Mapper**
  - Implement the `_new.AutoMapFrom(old)` utility within the state wrapper to automatically copy properties with matching names and types.

---

## Phase 3: Engine Execution & Routing
**Goal:** Update the core Orchestrator to support Side-by-Side (SxS) execution via ALC and to execute data migrations prior to instance resumption.

- [ ] **Task 3.1: AssemblyLoadContext (ALC) Isolation**
  - Implement a caching `AssemblyLoadContext` manager in the Runner Host.
  - When an older version instance wakes up, dynamically load the archived assembly into an isolated ALC context based on the version tag.
- [ ] **Task 3.2: Migration Router Interceptor**
  - Update the `WorkflowRunner` pipeline: before hydrating a suspended instance, check the global registry for a matching `WorkflowMigration` class.
  - If a migration class exists, route the instance data to the Migration Engine instead of the standard runtime execution path.
  - If no migration exists, route to the ALC for SxS execution.
- [ ] **Task 3.3: The Recursive Migration Walker**
  - Build the execution logic that invokes the migration class.
  - **Phase 1 (Instance):** Instantiate `TOld` and `TNew` wrappers, invoke `MigrateInstance`.
  - **Phase 2 (Waits):** Iterate over the active `WaitInfrastructureDto` collection. Invoke `MigrateActiveWait` for each.
  - **Phase 3 (Groups):** Implement recursive depth-first walking for `GroupWaitDto` children.
  - **Phase 4 (Sub-Workflows):** If `MigrateActiveWait` returns a `SubWorkflowWait`, automatically invoke `MigrateSubWorkflowState` on the child frame.
- [ ] **Task 3.4: Migration CFG Resolution**
  - For every `Wait` returned by the migration class, perform a lookup against the V2 `WorkflowName_Vxx_Schema.json` to resolve the compiler-generated `<>1__state` index.
  - Save the transformed state back to the database and queue any scheduled commands.

---

## Phase 4: Testing & Documentation
**Goal:** Ensure the system is robust against edge cases and clearly understood by the development team.

- [ ] **Task 4.1: Roslyn Testing**
  - Write unit tests for the CFG extractor using complex nested loops, `try/catch` blocks, and asynchronous groups to ensure the schema accurately reflects the AST.
- [ ] **Task 4.2: ALC Memory Leak Testing**
  - Write integration tests to ensure that ALCs can be unloaded or reused efficiently without leaking memory across thousands of executions.
- [ ] **Task 4.3: End-to-End Migration Test**
  - Build a comprehensive test using the `FulfillmentWorkflowMigration` scenario: renaming sub-workflows, collapsing groups, and passing data between `TOld` and `TNew`.
- [ ] **Task 4.4: Developer Documentation Update**
  - Publish the final Versioning guidelines to the team's internal developer portal.
