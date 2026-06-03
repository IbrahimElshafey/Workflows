Here is the updated architectural summary explaining how workflow versioning works in simple steps, incorporating your multi-workflow cleanup tool and plugin-based integration architecture.

---

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

To maintain an lean RAM footprint across thousands of running instances:

* **The Shared Core Layer:** Shared infrastructural dependencies (logging, database contexts, platform primitives) are loaded exactly once at the primary host layer.
* **The Pluggable Logic Layer:** Pluggable version folders capture and keep only the explicit business logic assemblies and distinct assets needed for that exact workflow iteration. All shared dependencies observe a strict **Law of Additive Contracts**—function signatures can expand, but existing boundaries are never broken or modified.

### 6. Strict "Birth-to-Death" Lifecycle Isolation

* **Zero Live Migration:** Active workflow instances never attempt a live structural code migration. Because the engine records durable snapshots (capturing local variables and the compiler-generated state machine index), forcing an active instance onto an updated class topology would instantly corrupt its state.
* **Quarantined Execution:** Every single instance is permanently locked to the exact compiled plugin assembly version it was instantiated under. It processes smoothly on its isolated track until it naturally hits implicit termination. Once the active instance tracking count for an archived generation reaches zero, that version plugin folder can be safely decommissioned from storage.