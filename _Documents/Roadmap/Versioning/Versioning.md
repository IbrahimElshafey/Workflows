# Workflow Versioning Architecture & Strategy

This document provides a comprehensive overview of the workflow versioning architecture, stability guardrails, and isolated runtime execution.

## Architectural Summary

### 1. Code Immutability & Plugin Separation
Instead of mixing infrastructure with business logic or polluting your code with inline version switching (`Workflow.GetVersion`), your active projects stay completely clean.
* **The Workflow Project:** Contains the user's workflow files. To integrate seamlessly with the framework, this project must implement a strict registration interface (`IWorkflowRegister` / `IWorkflowBuilder`).
* **Plugin Architecture:** This turns the user's project into a pluggable unit. The stateless `WorkflowRunner` treats these compiled versions as isolated plugins that can be discovered, swapped, and executed dynamically.

### 2. Multi-Workflow Source Generator Archiving
When a developer introduces a breaking change to a workflow and increments its version attribute, a custom Roslyn Source Generator steps in to automate the archiving. Because a single project can contain multiple workflows, the generator handles changes with surgical precision:
* It uses Roslyn to scan the entire project and identify all workflow classes.
* Through an IDE Code Fix, it moves the changed workflow into a version-suffixed folder (e.g., `/Archived/Billing_V1_0_0/`) and sets its **Build Action to None**.
* It alters the archived `.csproj` file (if necessary for ALC isolation) to inject a unique identity token (e.g., `<AssemblyName>MyCompany.Workflows.Billing.v1_0_0</AssemblyName>`), ensuring total type insulation.

### 3. Graph Stability & Schema Guardrails
To prevent developers from introducing silent runtime desynchronization errors to archived legacy code, each isolated version folder includes a strict contract called `WorkflowName_Vxx_Schema.json`.
During compilation, a continuous Analyzer hook checks the archived code using Roslyn's Semantic Model. It analyzes the workflow's **Control Flow Graph (CFG)** to map its linear basic blocks, execution branches, and chronological `yield return` checkpoint sequences. If the layout shifts or any underlying property type drifts from the recorded schema, the IDE instantly surfaces a fatal diagnostic error.

### 4. Isolated Runtime Execution via ALC
To run varying generations of identical class definitions simultaneously without naming collisions or runtime memory desynchronization:
* **Current Context:** The main hosting process loads your live, current production assemblies using the default .NET assembly loading layer.
* **Legacy Contexts:** When an old, sleeping workflow instance (e.g., version `1.0.0`) is awakened by an incoming signal, the engine initializes a dedicated, isolated **AssemblyLoadContext (ALC)**. It loads the archived pluggable `.dll` into this scoped memory zone. Because .NET identifies executing types using their absolute `AssemblyQualifiedName`, version `1.0.0` and version `2.0.0` run concurrently side-by-side without interference.

### 5. Shared Core vs. Isolated Primitives
To maintain a lean RAM footprint across thousands of running instances:
* **The Shared Core Layer:** Shared infrastructural dependencies (logging, database contexts, platform primitives) are loaded exactly once at the primary host layer.
* **The Pluggable Logic Layer:** Pluggable version folders capture and keep only the explicit business logic assemblies and distinct assets needed for that exact workflow iteration. All shared dependencies observe a strict **Law of Additive Contracts**—function signatures can expand, but existing boundaries are never broken or modified.

### 6. Strict "Birth-to-Death" Lifecycle Isolation
* **Zero Live Migration:** Active workflow instances never attempt a live structural code migration. Because the engine records durable snapshots (capturing local variables and the compiler-generated state machine index), forcing an active instance onto an updated class topology would instantly corrupt its state.
* **Quarantined Execution:** Every single instance is permanently locked to the exact compiled plugin assembly version it was instantiated under. It processes smoothly on its isolated track until it naturally hits implicit termination. Once the active instance tracking count for an archived generation reaches zero, that version plugin folder can be safely decommissioned from storage.

---

## Detailed Versioning Specifications

### 1. Unified Versioning Architecture Strategy

#### The Reality of Snapshot-Based Durable Engines

In traditional .NET state-machine suspension architectures, changing or recompiling workflow definitions creates an immediate, fatal mismatch between running data snapshots and updated assemblies. This occurs because minor text modifications—such as adding inline annotations, altering whitespace layouts, or appending unrelated logic tracks—cause the Roslyn compiler to restructure the underlying layout, index properties, and generated naming conventions of class closures. When a sleeping workflow instance attempts to rehydrate from a database store, the engine suffers unrecoverable variable desynchronization, schema mismatch exceptions, or structural process halts.

This engine bypasses this systemic fragility entirely through two structural platform invariants that decouple operational state from volatile compiler generation:

1. **Purity of State via Explicit Hand-off:** By enforcing the use of `.WithState(state)` within the fluent builder API, the engine bypasses internal compiler display classes (`<>c__DisplayClass`). Workflow lambda expressions are generated as stable, stateless function pointers, and instance state is tracked strictly as explicit, strongly typed data dataContracts attached directly to the active wait snapshot.
2. **Structural "Duck Typing" Hydration:** When a workflow resumes, the deserialization engine ignores raw compiler-generated state type names. It matches variable data payloads structurally against the fields of the currently loaded assembly via a name-based layout resolution matrix, providing extreme resilience against recompilations.

To support macro architectural changes—such as rearranging the sequence of workflow execution steps, introducing entirely new execution blocks, or altering framework dependency paths—the platform implements a **Single-Branch Native Project Isolation Strategy** (the Flat Multi-Project Layout). This allows distinct legacy versions to continue executing side-by-side on the identical active development branch, completely isolating production versions without incurring infrastructure code pollution or branch matrix debt.

---

### 2. Disabling the "One Workflow Per DLL" Constraint

#### Evolution Beyond Monolithic Folders

By transitioning to an automated multi-project disk layout, **the restrictive rule of "One Workflow per DLL" is completely eliminated**. The custom build and execution pipeline handles isolation at the **project/assembly boundary** rather than individual class footprints. A single, unified domain class library project (e.g., `MyCompany.Workflows.Billing`) can comfortably group multiple, distinct workflow definitions concurrently:

```text
📁 MyCompany.Workflows.Billing/
│   ├── 📄 InvoiceProcessingWorkflow.cs   (V1 Class definition)
│   ├── 📄 LatePaymentReminderWorkflow.cs  (V1 Class definition)
│   └── 📄 Billing.csproj                 (Unified multi-workflow compilation unit)

```

When changes are applied and the version is incremented, the custom build automation captures the entire project snapshot as a single unit. This ensures that related state-machine domains continue to compile and execute predictably in their native, co-dependent development context without risking structural breakage.

---

### 3. Flat Multi-Project Topography (Single-Branch)

All historical definitions and current development paths map directly onto a single development track (`main`/`develop`), cleanly segmenting active modules from legacy codebases.

```text
📁 MyRepositoryRoot/
│
├── 📁 Latest/                               <-- Current Dev Track
│   └── 📁 Billing/
│       ├── 📄 InvoiceProcessingWorkflow.cs
│       ├── 📄 LatePaymentReminderWorkflow.cs
│       └── 📄 Billing.csproj                 <-- The ONLY workflow included in Enterprise_Master.sln
│
├── 📁 Archived/                             <-- Auto-Generated Version Tracks
│   ├── 📁 Billing_V1_0_0/
│   │   ├── 📄 InvoiceProcessingWorkflow.cs
│   │   ├── 📄 LatePaymentReminderWorkflow.cs
│   │   ├── 📄 Billing_V1_0_0.csproj         <-- Suffix-mangled AssemblyName
│   │   ├── 📄 Billing_V1_0_0.sln            <-- Dedicated standalone patch workspace
│   │   └── 📄 version-manifest.json          <-- Structural stability contract
│   │
│   └── 📁 Billing_V1_1_0/
│       └── ...
│
├── 📄 Enterprise_Master.sln                 <-- Excludes all "/Archived/*" project trees
└── 📄 Billing_Runner_Host.csproj             <-- Entry-point runner executable platform

```

---

### 4. Side-by-Side Runtime Execution Context

To execute multiple overlapping generations of the same workflow class names side-by-side, the engine must run them concurrently without causing class collision errors or memory casting crashes within the common language runtime.

#### Isolated `AssemblyLoadContext` (ALC) Boundaries

The Runner Host shields the primary process domain by isolating each version track within its own dedicated, long-lived instance of a custom .NET `AssemblyLoadContext`.

* **The Primary App Context:** The core host process initializes utilizing the native `Default` assembly loading layer. This layer loads your active production assemblies (e.g., `MyCompany.Workflows.Billing.dll`) along with shared framework systems.
* **The Legacy Sub-Contexts:** When a sleeping instance bound to an older version (e.g., `1.0.0`) is awakened, the Runner checks its memory cache for an active ALC tagged with that specific version token. If it is a cold start, it creates an isolated ALC instance, reads the corresponding assembly files directly from the archived folder track, and mounts them inside that scoped memory zone.

#### Assembly Identity Mangling Mechanics

To ensure complete type separation, the build tool programmatically mangles the assembly metadata identifiers inside the auto-generated project files during the snapshotting phase:

```xml
<!-- Inside Archived/Billing_V1_0_0/Billing_V1_0_0.csproj -->
<PropertyGroup>
  <AssemblyName>MyCompany.Workflows.Billing.v1_0_0</AssemblyName>
  <RootNamespace>MyCompany.Workflows.Billing.v1_0_0</RootNamespace>
</PropertyGroup>

```

When this project compiles, it produces `MyCompany.Workflows.Billing.v1_0_0.dll`. Because .NET defines type identities using their structural **AssemblyQualifiedName** (e.g., `MyCompany.Workflows.Billing.InvoiceProcessingWorkflow, MyCompany.Workflows.Billing.v1_0_0`), the runtime can comfortably load, instantiate, and execute type metadata for both version `1.0.0` and version `2.0.0` simultaneously inside the same process space without type pollution or name intersection errors.

---

### 5. Shared Dependencies Context (Global Platform Layer)

To protect the server architecture from excessive RAM usage, the platform enforces a strict boundary between what libraries are **Shared Globally** and what layers are **Isolated on Disk**.

#### The Dependency Boundary Matrix

* **The Shared Core Layer (Default Context):** Massive foundational infrastructure components—such as logging extensions, RDBMS drivers, serialization engines, and your core framework primitives (`Workflows.Base`, `Workflows.Primitives`)—are promoted entirely to the **Host Process Level**. They are loaded exactly once in memory.
* **The Isolated Logic Layer (Version Subfolders):** The auto-generated version folder contains *only* the specific business logic assemblies, its unique expression structures, and private dependency assets exclusive to that explicit version generation.

#### The Law of Additive Contracts

Because legacy version projects reference your shared base primitives, you must strictly adhere to the **Law of Additive Contracts** within your core engineering layers:

* **Expanding Primitives:** Core engine abstractions (`IPassiveWait`, `ICommandWait`), core workflow states, and internal hydration properties can only grow. You may introduce new properties or provide non-breaking abstract method behaviors.
* **Signature Immutability:** You must **never delete or rename** an existing property, parameter, or method signature inside the shared core layers. If a foundational framework component requires a destructive architectural redesign, it must be introduced as an entirely new class or namespace identifier (e.g., `Workflows.Primitives.V2.IPassiveWait`), ensuring older project folders are completely insulated from breaking compilation drift.

---

### 6. The Stability Guard Workflow Schema

To prevent insecure or breaking modifications to legacy codebases during dependency resolution or manual conflict patching, each generated version folder contains a strict structural metadata snapshot named **`WorkflowName_Vxx_Schema.json`**.

This schema acts as a compile-time contract, mapping out the precise stable state-contract boundaries, property names, variable data types, and wait identifiers that the workflow version requires to remain valid:

```json
{
  "PackageVersion": "1.0.0",
  "TargetAssemblyName": "MyCompany.Workflows.Billing.v1_0_0",
  "Workflows": {
    "InvoiceProcessingWorkflow": {
      "StateProperties": [
        { "Name": "OrderId", "Type": "System.Guid" },
        { "Name": "CustomerId", "Type": "System.String" },
        { "Name": "CurrentTotal", "Type": "System.Decimal" }
      ],
      "WaitContracts": [
        {
          "LineNumber": 45,
          "MethodName": "ExecuteWorkflowAsync",
          "WaitType": "SignalWait",
          "SignalIdentifier": "InvoicePaidSignal",
          "PayloadType": "MyCompany.Contracts.InvoicePaidData"
        }
      ]
    },
    "LatePaymentReminderWorkflow": {
      "StateProperties": [
        { "Name": "RetryCount", "Type": "System.Int32" }
      ],
      "WaitContracts": [
        {
          "LineNumber": 12,
          "MethodName": "ExecuteWorkflowAsync",
          "WaitType": "TimeWait"
        }
      ]
    }
  }
}

```

#### The Invalidation Safeguard Engine

When a developer updates a core business DTO or modifies a shared class library, running the validation utility parses the legacy folder via Roslyn to compare the new project code state against this schema. It explicitly halts builds if it encounters:

1. **Type-Drift Infractions:** Modifying, deleting, or down-casting the types of variables recorded in `StateProperties`.
2. **Wait Signature Shift:** Moving, removing, or changing the `SignalIdentifier` contract properties of an active checkpoint wait.
3. **Incompatible Schema Modifiers:** Altering structural signatures that would cause existing serialized database instances to crash or fail upon hydration.

---

### 7. Developer Proactive Incompatibility Alerts

To guide developers in real-time, the platform integrates **Workflows.Analyzers** directly into the compilation cycle. Rather than allowing developers to discover structural breaks late in the deployment loop, the engine surfaces semantic constraints directly inside the Visual Studio IDE using explicit Roslyn-driven diagnostics.

#### The Comprehensive Analyzer Diagnostic Matrix

##### Category A: The Foundation Layer (Priority 1)

* **WF000: Strict No-Closure Violation**
* *Trigger:* A developer passes an inline lambda expression to any Wait builder method (e.g., `.MatchIf()`, `.AfterMatch()`) that is **not** explicitly marked with the `static` keyword.
* *Action:* **CRITICAL COMPILER ERROR**. Halts the local build.
* *Remediation:* Forces the developer to utilize explicit state parameters via `.WithState(state)` or map variables directly to `WorkflowContainer` class properties to ensure the lambda remains pure, static, and deterministic.



##### Category B: Serialization & State Isolation

* **WF001: Direct Sub-Workflow Execution Failure**
* *Trigger:* Finding an `await foreach` loop that natively enumerates an `IAsyncEnumerable` sub-workflow.
* *Action:* **ERROR**. Forces refactoring to yield execution safely to the Runner via `yield return WaitSubWorkflow(...)`.


* **WF002: Volatile Transient Resource Capture**
* *Trigger:* Declaring or assigning an `IDisposable` resource type (e.g., `SqlConnection`, `Stream`, `HttpClient`) across a suspension boundary inside the main execution flow.
* *Action:* **ERROR**. Ephemeral resource handles cannot be serialized and will crash the engine state manager during dehydration.


* **WF003: Suspended Context Inside Locking Blocks**
* *Trigger:* A `yield return` statement is located inside a `using` synchronization syntax lock block.
* *Action:* **ERROR**. The engine cannot drop execution threads and save state while actively maintaining a framework resource lock.


* **WF005: Synchronous Method State Blindspot**
* *Trigger:* Declaring a local variable inside a *synchronous* helper method (e.g., `private SignalWait GetWait() { var id = 5; return WaitSignal(...); }`) and passing that variable to a Wait closure.
* *Action:* **ERROR**. Because the helper method is not asynchronous, Roslyn fails to lift the local variable into an assembly-persisted field. The variable vanishes completely when the workflow resumes.



##### Category C: Determinism & Thread Safety

* **WF101: Non-Deterministic Operation Capture**
* *Trigger:* Invoking non-deterministic system properties like `DateTime.UtcNow`, `Guid.NewGuid()`, or `Random.Next()` directly inside the workflow execution thread.
* *Action:* **WARNING**. Recommends yielding a Command or passing the data via incoming signals to preserve determinism during time-traveling debug sessions or Saga rollbacks.


* **WF102: Prohibited State Mutation in Helper Contexts**
* *Trigger:* A private, synchronous helper method alters a root class-level property (e.g., `this.TotalAmount += 50;`).
* *Action:* **ERROR**. All state mutations must occur strictly inside the main `IAsyncEnumerable` thread or within explicit `.AfterMatch` blocks to safely align with snapshotting boundaries.



##### Category D: Routing & Framework Boundaries

* **WF201: Open Workflow Container Declaration**
* *Trigger:* A class extending `WorkflowContainer` is not explicitly marked as `sealed`.
* *Action:* **ERROR**. Classes must be sealed to guarantee perfect type resolution when the Orchestrator reconstructs executions from assembly strings.


* **WF203: Impure Match Expression Layer**
* *Trigger:* Utilizing custom method executions or black-box API checks (e.g., `ExternalDb.Verify(signal.Id)`) within a `.MatchIf()` parameter.
* *Action:* **WARNING**. Notifies the developer that the custom method cannot be parsed into a fast SQL query index or optimized JsonElement pre-filter, forcing the system to fall back to unoptimized RAM-heavy processing.



---

### 8. The Source Generator Integration

The versioning automation is driven directly by the .NET compiler via a custom **Roslyn Source Generator and Code Analyzer**, ensuring that local development workflows, compilation boundaries, and runner nodes share a completely unified dependency model without external CLI tools.

#### The Core Execution Pipeline

##### 1. Triggering Version Increments

This workflow is entirely IDE-driven:

* **Syntax Scan (Roslyn-Driven Discovery):** The Analyzer constantly monitors class signatures implementing `WorkflowContainer`. When the developer changes the version attribute (e.g., `[WorkflowVersion("2.0.0")]`), the Analyzer flags a diagnostic warning: *"Version incremented. Archive previous version."*
* **Code Fix Provider Action:** The developer applies the IDE Code Fix (lightbulb). The provider automates the structural changes.
* **Directory Mirroring Automation:** The original `.cs` file is moved into a version-suffixed repository directory path: `/Archived/Billing_V1_0_0/` and its **Build Action** is set to **None**. This removes it from active compilation while preserving it as an audit trail.
* **Contract Schema Creation:** The generator serializes the stable property definitions, CFG layouts, and wait states, outputting the `WorkflowName_Vxx_Schema.json` contract file into that subfolder.
* **Migration Layout Generation:** The source generator reads the schema and dynamically emits a `WorkflowNameVxx_Layout.cs` class. This provides the strictly typed wrapper (`TOld`) needed for the migration API, ensuring perfect type alignment.

##### 2. Continuous Verification

This verification acts as an un-bypassable gate, executed locally by developers as they type, and enforced during compilation in CI/CD pipelines.

* **The Execution Mechanics:** The Analyzer continuously parses the archived C# files (even if Build Action is None) and validates their structural integrity against the generated `WorkflowName_Vxx_Schema.json` contract file.
* **Clickable IDE Feedback Loop:** If an incompatibility or compilation error is caught, the Analyzer emits a standard compiler diagnostic directly to the IDE Error List:
```text
C:\Workspace\Billing\Archived\Billing_V1_0_0\InvoiceProcessingWorkflow.cs(45,12): error WF3001: Property 'InvoiceTotal' layout mismatch against schema contract in InvoiceProcessing_V1_Schema.json.
```

Because this is a native compiler diagnostic, **the developer simply double-clicks the error inside their Visual Studio Error List.** The IDE automatically brings up the file and maps the cursor to the exact line.
---

### 9. Heterogeneous Group Coordination Logic

When your workflow moves past basic execution paths, it inevitably encounters complex coordinating structures where passive waits and active commands are combined within a unified structure:

```csharp
yield return GroupWait.MatchAny(
    WaitSignal<InvoicePaidData>("InvoicePaidSignal"),
    ExecuteDeferred(new SendReminderEmailCommand(Id), "EmailKey"),
    WaitTime(TimeSpan.FromDays(2))
);

```

To support this flexibility natively without causing runtime conflicts or tracking bugs, the engine processes the group via a centralized **Heterogeneous Group Coordinator** pattern.

#### Phase 1: The Fan-Out Evaluation (Yielding the Group)

When the stateless `WorkflowRunner` thread encounters a `GroupWait` containing mixed implementations of `IPassiveWait` and `ICommandWait`, it handles them synchronously:

1. **Decomposition:** The Group Coordinator decomposes the child collection array.
2. **Active Intent Dispatch:** It loops through any `ICommandWait` elements (like `DeferredCommand`), passing them straight to the `ICommandHandlerFactory` to be immediately published onto your out-of-process enterprise message bus.
3. **Passive Boundary Registration:** It loops through any `IPassiveWait` elements (like `SignalWait` or `TimeWait`), registering them as active database listeners inside the Orchestrator's SQL tables[cite: 1, 2].
4. **Suspension Execution:** Because the group relies on asynchronous external responses, the Runner breaks local execution, packages the parent group synchronization identity token, and returns control to the persistence layer.

#### Phase 2: Slot Tracking (Partial Satisfaction)

The state of the group remains suspended in the database until a child component criteria event returns.

1. When an event hits the Orchestrator—whether it is an incoming business signal, a timer expiration, or a deferred command result callback—the system intercepts the record.
2. It loads the corresponding `WorkflowRunContext` and locates the specific child index identifier within the parent `GroupWait` structure.
3. It updates that specific row slot state to `Completed`.
4. It immediately executes the core group evaluation policy (`MatchAll`, `MatchAny`, or a custom conditional expression).
5. **The Resumption Gateway:** The main state machine is only reawakened and permitted to advance if the overarching group coordination logic evaluates to true.

#### Phase 3: The Group Pruning Sweep (Clean-up)

If the evaluation policy is satisfied (e.g., a `TimeWait` fires first inside a `MatchAny` block), the Group Coordinator initiates an immediate, non-blocking **Pruning Command** to neutralize the remaining branches:

* **For Passive Listeners:** It commands the Orchestrator to delete the open database rows from the `SignalWaits` table, preventing future rogue signals from hitting that obsolete wait point[cite: 1, 2].
* **For Active Dispatched Commands:** It logs the command's correlation identifiers as `Canceled/Abandoned` in the engine registry. If the external service later publishes a late success message back up the wire, the Orchestrator reads the tracking flag, recognizes it as an orphaned execution result, and discards it safely without polluting the advanced state machine.

---

### 10. Architectural Evaluation: Why This System Excels

| Operational Metric | Traditional Systems (Temporal / Azure Durable) | Unifed Flat Multi-Project Layout |
| --- | --- | --- |
| **Code Pureness** | Highly Polluted. Requires boilerplate branching arrays inside your workflow methods (`Workflow.GetVersion`). | Perfect. Code remains clean C# business logic; versioning tracking happens at the project infrastructure layer. |
| **State Rehydration Performance** | Heavy Overhead. Must reload and replay entire event histories from line 1 every time a step wakes up. | Instantaneous. Bypasses history replays entirely, hydrating variables natively into memory fields in milliseconds. |
| **Repository Management** | Complex. Forces you to manage separate worker code tags, complex branch matrices, or external package feeds. | Flat & Simple. Everything is cleanly tracked, compiled, and maintained on a single development branch. |
| **IDE Diagnostics Support** | Low. Breaking contract changes are often caught late during integration or runtime testing phases. | Real-Time. Your custom Roslyn analyzers surface error markers inside the code window during active editing. |
| **Memory Footprint Optimization** | High RAM Waste. Separate plugin instances load full framework libraries repeatedly, exhausting memory profiles. | Lean Runtime. Shared primitives load once at the host level, dropping footprint memory profiles to near zero. |