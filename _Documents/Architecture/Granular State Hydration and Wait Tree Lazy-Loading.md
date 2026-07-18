# Granular State Hydration and Wait Tree Lazy-Loading

This document details the architectural design for path-based wait tree lazy-loading and granular state hydration, optimizing database I/O and memory footprints under massive wait trees and nested sub-workflows.

---

## 1. Context & Problem Statement

Currently, advancing a workflow instance requires a monolithic load of its execution state:
1.  **Full Wait Tree Deserialization:** The entire hierarchical waits tree (`state.Waits`), containing all completed, canceled, and sleeping nodes, is loaded and parsed from the database.
2.  **Monolithic State Hydration:** All local variables and transient properties inside `StateObject.Locals` are serialized and deserialized as a single, large JSON blob.

Under high-concurrency, complex parallel fan-outs (`WaitMany`), or deeply nested sub-workflows, this monolithic strategy leads to:
*   **High CPU Overhead:** Heavy serialization/deserialization costs for large JSON blobs.
*   **Memory Pressure:** Loading thousands of sleeping variables into RAM when only a single branch is advancing.
*   **Write Contention:** Overwriting the entire state blob on every step increases database write-lock footprints.

---

## 2. The Granular Hydration Proposal

The proposed solution replaces the monolithic load with **targeted, path-based hydration** of wait nodes and local variables.

```
Monolithic Load (Current):
[Database] ──(Entire JSON Context)──> [Deserializer] ──> [Runner (All Locals in RAM)]

Granular Load (Proposed):
[Database] ──(Relational Match: WaitId)
                  │
                  ├──(Load Leaf + Ancestors Only) ──> [Runner (Only Active Locals)]
```

### Key Principles:
1.  **Lazy-Load Ancestor Paths:** When a signal, timer, or command callback matches a leaf wait ID, the Orchestrator queries the database to load *only* that leaf wait and its direct ancestors up to the root, ignoring all other sibling branches.
2.  **Partitioned Locals:** Instead of storing all local variables in a single dictionary blob in `WorkflowInstances`, local variables are stored as individual database rows keyed by the specific `WaitId` or `StateKey` to which they belong.
3.  **Active-Path Hydration:** The engine only hydrates the subset of state variables associated with the active path. Sleeping branches remain serialized in the database.

---

## 3. Database Schema Shift

To support granular hydration, we decouple wait-state data from the primary `WorkflowInstances` table.

### 3.1. The `WorkflowWaitStates` Table (New)
This table holds JSON fragments containing state variables for individual wait nodes:

```sql
CREATE TABLE WorkflowWaitStates (
    WaitId NVARCHAR(100) PRIMARY KEY,
    WorkflowInstanceId UNIQUEIDENTIFIER NOT NULL,
    StateKey NVARCHAR(100) NOT NULL,
    SerializedState NVARCHAR(MAX) NOT NULL, -- JSON payload for this wait's state only
    LastUpdated DATETIME NOT NULL,
    FOREIGN KEY (WorkflowInstanceId) REFERENCES WorkflowInstances(Id) ON DELETE CASCADE
);
```

### 3.2. Refactoring `WorkflowInstances`
The `StateObject` column in the `WorkflowInstances` table is stripped of its child `Locals` dictionary. It now holds only:
*   Root workflow properties.
*   The global execution status.
*   Lightweight metadata.

---

## 4. Execution Flow & Path Parsing

When an event triggers the Orchestrator, execution progresses through the following stages:

### Step 1: Relational Pre-Filter Match
The incoming event is matched against the database indexes (`SignalWaits`, `TimeWaits`, `CommandWaits`, or `ExternalChildWaits`) to resolve the triggering `WaitId`.

### Step 2: In-Memory Ancestor Path Parsing
Because the engine formats the `WaitId` hierarchically (e.g. `leafId/parentId/rootId/instanceGuid`), **the Orchestrator does not need to query the database to reconstruct the hierarchy**. It splits the path string in memory:
```csharp
// Example: "4/3/2/1/instance-guid"
var waitPathId = triggeringWaitDto.Id;
var ancestorIds = ParseAncestorIds(waitPathId); 
// Results: ["4/3/2/1/instance-guid", "3/2/1/instance-guid", "2/1/instance-guid", "1/instance-guid"]
```

### Step 3: Granular State Hydration (Single Query)
Using the parsed list of ancestor IDs, the Orchestrator fetches only the specific wait states and local variable JSON fragments in a single flat SQL query:
```sql
SELECT * FROM WorkflowWaitStates 
WHERE WorkflowInstanceId = @InstanceId 
  AND WaitId IN (@TriggeringWaitId, @AncestorId1, @AncestorId2, ...);
```
Only these active path JSON fragments are deserialized into memory.

### Step 4: Step Advancement
The `WorkflowRunner` executes, advancing the C# state machine using the hydrated active context path.

### Step 5: Granular Write-Back
Upon yielding:
1.  Newly generated wait nodes are flattened and inserted.
2.  The state of the completed wait node is deleted from `WorkflowWaitStates`.
3.  The state of any new active wait nodes is written to `WorkflowWaitStates`.
4.  Unchanged sleeping wait states are left completely untouched in the database.

---

## 5. Evaluation & Trade-offs

### Pros
*   **$O(\text{Path Depth})$ Performance:** Memory consumption and serialization latency scale with the depth of the active execution branch, not the total size of the workflow.
*   **Minimal Database Blobs:** Database writes are tiny JSON fragments rather than monolithic payload blobs.
*   **Optimistic Concurrency Optimization:** Since sibling branches update different rows in `WorkflowWaitStates`, concurrent completions inside a `WaitGroup` have a much lower chance of throwing DB update conflicts.

### Cons & Challenges
*   **Ancestor Context Dependency:** If a C# state machine resumes, the compiler-generated code might expect parent variables to be present in scope. The engine must ensure that all parent/ancestor variables along the active path are hydrated.

---

## 6. Special Handling & Optimization (Cancellation, Compensation & Group Waits)

Transitioning to path-based lazy-loading unlocks significant performance improvements for cancellation, compensation, and group coordination by shifting logic to relational queries:

### 6.1. Relational Cancellation
*   **The Problem:** Traditional cancellation requires traversing the full waits tree in memory to find and cancel nodes containing the target cancellation token.
*   **The Optimization:** Instead of loading the tree, the `CancelerWorker` executes a direct relational update to flag canceled nodes:
    ```sql
    UPDATE SignalWaits 
    SET Status = 400 -- Canceled
    WHERE WorkflowInstanceId = @InstanceId 
      AND Status = 100 -- Waiting
      AND CancelTokens LIKE @CancelTokenFilter;
    ```
    This marks all matched nodes as `Canceled` in one operation, completely bypassing memory-based traversal.

### 6.2. Direct LIFO Compensation Query
*   **The Problem:** The `CompensationWorker` originally had to parse the full wait tree to locate completed commands containing the compensation token and reverse them in memory.
*   **The Optimization:** The worker queries the `CommandWaits` table directly, utilizing SQL sorting to get the LIFO unwind queue:
    ```sql
    SELECT Id, CommandWaitId, HandlerKey, CommandResult 
    FROM CommandWaits
    WHERE WorkflowInstanceId = @InstanceId 
      AND Status = 200 -- Completed
      AND CompensationTokens LIKE @CompensationTokenFilter
    ORDER BY CompletedAt DESC; -- LIFO ordering handled directly by RDBMS
    ```
    This simplifies the worker loop and guarantees correct rollback order.

### 6.3. Relational Group Wait Evaluation
*   **The Problem:** When a child wait completes, the engine needs to evaluate if the parent `WaitGroup` (e.g. `WaitAll`) is satisfied.
*   **The Optimization:** 
    1.  The Orchestrator extracts the parent wait ID in-memory from the path string (e.g., parent of `4/3/2/1/instance-guid` is `3/2/1/instance-guid`).
    2.  It executes a fast relational count query to check the completion status of sibling waits under that parent group:
        ```sql
        SELECT Status, COUNT(*) 
        FROM SignalWaits 
        WHERE ParentWaitId = @ParentGroupId 
        GROUP BY Status;
        ```
    3.  If the counts indicate that all child waits are satisfied (or match the group's specific logical policy), the parent group is marked as completed in-memory and committed. This keeps group evaluation extremely fast and decoupled from the main JSON state.
