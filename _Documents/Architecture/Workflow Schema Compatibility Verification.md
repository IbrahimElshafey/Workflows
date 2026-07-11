# Workflow Schema Compatibility Verification

This document specifies the design for a **Static Analysis CLI Tool** powered by Roslyn AST parsing to detect breaking changes in C# workflow definitions before deployment, safeguarding in-flight instances from rehydration failures.

---

## 1. The Challenge: State Machine Drifts

Because this engine serializes the compiler-generated C# state machine index (`<>1__state`) and local variables directly, modifying a workflow container class (e.g., adding, removing, or reordering `yield return` waits) can break state compatibility:

*   **State Index Shift**: Adding a new `yield return` in the middle of a workflow shifts all subsequent compiler states. If an in-flight workflow instance resumes, the runner will jump to the wrong execution block, causing unpredictable behavior or runtime exceptions.
*   **Variable Type Drift**: Modifying the C# class type or field names of serialized variables leads to JSON deserialization failures when loading older instance snapshots.

---

## 2. Compatibility Verification CLI Design

To catch compatibility breaks during CI/CD builds, we design a CLI tool: `dotnet-wf-verify`.

```
[New Code Build] ──► [dotnet-wf-verify] ── (Parses ASTs)
                            │
                            ├─► Compare AST of new code vs. reference/master branch assembly
                            │
                            ├── No Shifts Detected  ──► [Pass CI/CD Build]
                            │
                            └── Breaks Detected     ──► [Fail Build / Force Version Bump]
```

### Command Execution
```bash
dotnet wf-verify --assembly-old ./bin/Release/net10.0/Workflows.dll --assembly-new ./bin/NewRelease/net10.0/Workflows.dll
```

---

## 3. Roslyn Abstract Syntax Tree (AST) Analysis

The CLI tool loads both assembly versions and leverages Roslyn's Semantic Model to extract the **Wait Topology** of each workflow.

### 1. Wait Topology Mapping
For each `WorkflowContainer`, the analyzer maps:
*   The sequence of `yield return` statements.
*   The argument type (e.g., `SignalWait<T>`, `TimeWait`, `GroupWait`).
*   The hardcoded Wait Names / Correlation Path keys.

### 2. Detection Rules
| Change Type | Risk Level | Description | Rule |
| :--- | :--- | :--- | :--- |
| **Insert Wait** | 🔴 Critical Break | Inserting a wait statement in the middle of an existing path. | **Fails Verification**: Requires a new workflow registration ID or version routing bump. |
| **Delete Wait** | 🔴 Critical Break | Deleting an existing wait statement in an active path. | **Fails Verification**: Breaks the rehydration state index of running instances. |
| **Append Wait** | 🟢 Safe | Appending a new wait *at the end* of the execution path. | **Permitted**: Does not affect prior state indexes. |
| **Rename Property** | 🟡 Warning / Break | Renaming a workflow container public property. | **Fails Verification**: Unless decorated with a `[JsonPropertyName]` redirect attribute to preserve JSON parsing. |

---

## 4. Remediation Strategies for Breaks

When the static verification tool detects a breaking change, the developer must employ one of the following remediation patterns:

### A. Side-by-Side (SxS) Versioning
Instead of modifying the class in-place, create a new workflow class or bump the version parameter in the `Workflow` attribute:
```csharp
// Old Class (Kept for in-flight instances)
[Workflow("OrderWorkflow", 1)]
public class OrderWorkflowV1 : WorkflowContainer { ... }

// New Class (Used for new instances)
[Workflow("OrderWorkflow", 2)]
public class OrderWorkflowV2 : WorkflowContainer { ... }
```

### B. Explicit State Migration
If instances must be migrated, the developer writes a migration script to update the serialized JSON state values in the database, mapping the old state indexes to the new index configuration.
