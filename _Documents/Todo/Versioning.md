https://gemini.google.com/app/b277b8b553fc735f

### Phase 1: Developer API & Attribute Routing (Workflows.Base)

**Goal:** Allow developers to define versions cleanly without cluttering their class names, and allow safe side-by-side execution in the same DLL.

**1. Create the `WorkflowAttribute`:**

* Add a new `[Workflow(Name = "MyWorkflow", Version = "1.0.0")]` attribute.
* This separates the *logical identity* from the C# class name.

**2. Update Roslyn Analyzers (WF201 Validation):**

* Ensure the `WorkflowContainer` classes are marked as `sealed`.
* Ensure every `WorkflowContainer` has the `[Workflow]` attribute attached. If missing, throw a compile-time error.

**3. Example Code Target:**

```csharp
namespace App.Workflows
{
    [Workflow(Name = "OrderProcessing", Version = "1.0.0")]
    public sealed class OrderProcessingV1 : WorkflowContainer { ... }

    [Workflow(Name = "OrderProcessing", Version = "2.0.0")]
    public sealed class OrderProcessingV2 : WorkflowContainer { ... } // Safe side-by-side
}

```

---

### Phase 2: Runner-Side Topology Extraction

**Goal:** Keep the Orchestrator fast and reflection-free by making the Runner responsible for extracting its own structural schema at startup.

**1. Assembly Scanning Service:**

* Implement `RegisterFromAssemblyContaining<T>()` in the Runner's Dependency Injection setup.
* It will scan for all sealed classes inheriting from `WorkflowContainer`.

**2. Generate the JSON Topology Schema:**

* Create a `TopologyExtractor`. It will use reflection (only once at cold start) to inspect the `ExecuteWorkflowAsync()` method and sub-workflows.
* It will generate a static structural JSON map of the exact sequence of yields (`WaitSignal`, `TimeWait`, `SubWorkflowWait`).

**3. Prepare the Registration DTO:**

* Update `WorkflowRegistrationInput` to include `WorkflowName`, `Version`, `AssemblyQualifiedName`, and `WorkflowTopologySchemaJson`.

---

### Phase 3: The Orchestrator Verification Handshake

**Goal:** The Orchestrator acts as the "Traffic Cop" and Database owner, verifying contracts before allowing the Runner to connect to the cluster.

**1. The Handshake Protocol:**

* When the Runner boots, it sends a bulk registration payload (the DTOs) to the Orchestrator via `IMessageDispatcher`.

**2. Implement the Orchestrator Verification Logic:**
I will build an `IRegistrationValidator` inside the Orchestrator with three strict pathways:

* **Case A (New Deployment):** If `WorkflowName` + `Version` doesn't exist in the DB, accept it, save the JSON schema, and mark it as the active route.
* **Case B (Scale-out / Reboot):** If `WorkflowName` + `Version` exists, perform a strict string equality check on the JSON schema. If they match, accept the connection (it's just a redundant node booting up).
* **Case C (Illegal Mutation - Hotfix Trap):** If the Version exists but the JSON schema is *different*, **reject the registration** immediately. Return a critical error: `"WF3001: Structural mutation detected on active deployment version. Increment your version token."`

---

### Phase 4: Database schema & Runtime Routing

**Goal:** Update the storage adapters and routing logic so incoming webhooks hit the correct code block.

**1. Update `IDefinitionStore` / DB Schema:**

* Update the `WorkflowRegistrations` SQL table to include `Version` and `TopologySchema` columns.
* Create a composite unique index on `(WorkflowName, Version)`.

**2. Update `WorkflowInstances` Table:**

* Ensure every running workflow instance has a strict foreign key (or stored string) linking it to its specific `Version`.

**3. Routing the Signals:**

* When a generic signal (e.g., `OrderSubmitted`) arrives, the Orchestrator queries the `SignalWaits` table.
* Because the `SignalWaits` table joins with `WorkflowInstances`, the Orchestrator intrinsically knows that Instance #999 needs `OrderProcessing v1.0.0`.
* It packages the `WorkflowRunContext` and sends it to the Runner. The Runner's cache will seamlessly invoke `OrderProcessingV1` instead of `V2`.

---

### Summary of Tasks for the Sprint Board:

1. **Task 1:** Add `WorkflowAttribute` and update `WorkflowRegistrationInput` DTOs.
2. **Task 2:** Build `TopologyExtractor` on the Runner to generate structural JSON blueprints at startup.
3. **Task 3:** Implement the Orchestrator DB Validation (Reject mutations on active versions).
4. **Task 4:** Update standard routing queries to use the dual-key (`Name` + `Version`) resolution logic.