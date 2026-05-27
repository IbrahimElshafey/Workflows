Here are the synthesized notes for your "Workflows Versioning & Compatibility" documentation. You can copy and paste these directly into your `Workflows_Versioning_Design.docx` file under a "Design Principles & Technical Notes" section.

---

### **Design Principles & Technical Notes**

* **The Immutable Binary Constraint:**
* Workflow assemblies are treated as immutable binary units. Once a workflow version is deployed to production, its binary logic must never be mutated in-place.
* Running workflow instances are "pinned" to the specific assembly version that initiated them, ensuring execution continuity and preventing state corruption.


* **Contract-First Compatibility:**
* The `Workflows.Contracts` DLL serves as the hard contract between the Host and the Workflow.
* Versioning is managed by enforcing build-time compatibility: if the `Workflows.Contracts` interface surface changes (breaking changes), the workflow binary **must** be recompiled against the new contract version, even if the internal business logic remains identical.


* **Isolation via `AssemblyLoadContext`:**
* To support side-by-side execution of different workflow versions within the same process (without the overhead of multi-processing), each workflow version is loaded into its own isolated `AssemblyLoadContext` (ALC).
* This prevents type collision errors when different versions share the same class names or rely on different versions of shared dependencies.


* **Compile-Time Guardrails (Roslyn Analyzers):**
* Runtime serialization exceptions are treated as "too late" in the lifecycle.
* **WF1001 (Contract Incompatibility):** Blocks compilation if the workflow relies on removed/changed interfaces in `Workflows.Contracts`.
* **WF1002 (Illegal State Capture):** Blocks compilation if local variables or closures cannot be safely serialized, forcing the use of explicit state patterns (`.WithState(T)`).


* **Automated Verification Pipeline:**
* The Runner Host exposes a `/manifest` endpoint providing the current platform signature (contract version + assembly hashes).
* CI/CD pipelines perform an automated comparison between the workflow's dependency requirements and the platform manifest, breaking the build on any detected incompatibility.


* **State Machine Determinism:**
* The engine avoids Event Sourcing/Replay complexity by using C# native `IAsyncEnumerable` state machine snapshots.
* The `StateMachineAdvancer` treats the `<>1__state` integer as the only stable anchor for resumption, treating all other compiler-generated field names as volatile implementation details that are handled via dynamic field-mapping rather than brittle reflection-based serialization.


* **Governance & Lifecycle Management:**
* **Phased Retirement:** When a versioned workflow definition is marked as `Dead` in the Orchestrator, new signals are no longer routed to it.
* **Drain-to-Completion:** Existing instances of a `Dead` version are allowed to finalize naturally; once the instance count hits zero, the Orchestrator safely triggers the ALC unload process to purge the assembly and its associated memory footprint.



---