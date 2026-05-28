Based on your specifications, here is the new architectural section for **Section 8 (The Invalidation Safeguard Engine)**, detailing how the system utilizes the Control Flow Graph (CFG) to protect old workflow versions.

---

### 8.3 Control Flow Graph (CFG) Structural Verification

To ensure that the execution layout of legacy workflows remains backward-compatible across modifications, the validation utility moves beyond simple text or field reflection. It evaluates the method’s **Control Flow Graph (CFG)** via the Roslyn Semantic Model API (`context.SemanticModel.AnalyzeControlFlow(methodBody)`).

This enforces a strict graph-isomorphism validation gate, ensuring the topological sequence of state-machine jumps, branching conditions (`if`/`else`), loop iterations (`while`/`for`), and rich `Wait` yields matches the original compilation signature exactly.

#### 1. Topological Graph Extraction Blueprint

During the `dotnet workflow build` phase, the compiler breaks the workflow down into **Basic Blocks** (linear sequences of executable instructions with a single entry and exit point). The analyzer records this network of basic blocks as a structural map within `version-manifest.json`:

```json
{
  "WorkflowName": "MyCompany.Workflows.Billing.v1_0_0.InvoiceProcessingWorkflow",
  "ControlFlowTopology": {
    "TotalBasicBlocks": 4,
    "ExecutionEdges": [
      { "FromBlockIndex": 0, "ToBlockIndex": 1 },
      { "FromBlockIndex": 1, "ToBlockIndex": 2 },
      { "FromBlockIndex": 1, "ToBlockIndex": 3 },
      { "FromBlockIndex": 2, "ToBlockIndex": 3 }
    ],
    "BlockPayloads": {
      "Block_1": [
        {
          "WaitType": "MyCompany.Workflows.Waits.ApprovalWait",
          "EvaluatedProperties": {
            "Name": "ManagerApproval",
            "TimeoutDays": "3"
          }
        }
      ],
      "Block_2": [
        {
          "WaitType": "MyCompany.Workflows.Waits.DelayWait",
          "EvaluatedProperties": {
            "Duration": "00:05:00"
          }
        }
      ]
    }
  }
}

```

#### 2. Resolving Rich Wait Properties and Inline Methods

To validate the payload details of each state suspension step, the analyzer intercepts every `YieldStatementSyntax`. It combines Semantic Model type-resolution with **Constant Expression Evaluation** to safely extract the data configuration of your rich `Wait` objects:

* **Object Initializers & Constructors:** Whether a developer declares `new ApprovalWait { Name = "ManagerApproval" }` or calls a helper factory method like `yield return GetApprovalWait();`, the Semantic Model resolves the underlying output type back to its fully qualified name (`MyCompany.Workflows.Waits.ApprovalWait`).
* **Constant Value Evaluation:** The analyzer evaluates the right-hand expressions or arguments passed to the `Wait` properties using `semanticModel.GetConstantValue()`. These values are stored as stringified key-value pairs under `EvaluatedProperties`.

#### 3. Execution Pass/Fail Validation Invariants

When `dotnet workflow verify` executes, the analyzer constructs a live CFG graph of the current code and compares it directly against the stored JSON block map.

The change is classified as **Safe (Compilation Allowed)** if modifications are localized entirely inside the basic blocks without touching yield points:

* Modifying pure local calculations (e.g., changing `x = x + 1` to `x++`).
* Adding, modifying, or removing code statements, logger tracking, or business logic that does not alter the execution branches or input properties of a `Wait` step.

The change is classified as **Unsafe (Compilation Failed)** and emits a fatal diagnostic error if it violates graph stability:

```text
error WF3002: Control Flow Graph (CFG) structural drift detected in 'InvoiceProcessingWorkflow.cs'. Live basic block layout or branching paths do not match the historical version-manifest.json file.

```

* **Infratruction 1: Edge and Block Drift:** Adding or removing a branching condition (`if`/`else`), short-circuiting an expression, or altering loop structures (`while`/`for`). This alters the number of Basic Blocks or `ExecutionEdges`, shifting the compiler's generated switch case map.
* **Infraction 2: Sequence Mismatch:** Rearranging the chronological order of `yield return` checkpoints inside a basic block, or moving a `yield return` from one basic block into an adjacent branch.
* **Infraction 3: Property Mutation:** Altering a constant literal configuration value bound to an active wait (e.g., changing `TimeoutDays` from `"3"` to `"5"` inside an archived workflow code block).