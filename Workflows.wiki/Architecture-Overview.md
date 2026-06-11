# Architecture Overview

The Workflows Engine is architected on a strict separation of concerns between **Domain Definition**, **I/O & Persistence**, and **Compute & Execution**. This design enables high scalability and allows the engine to run either entirely in-process or distributed across a cluster.

---

## 1. Core Philosophy

*   **Zero-Dependency DSL:** Workflow definitions reside in a pure standard library (`Workflows.Definition`) with no knowledge of databases, message brokers, or host setups.
*   **No Replay Overhead:** Traditional workflow frameworks replay your code's entire history from event-sourcing logs to reconstruct local state variables. This engine directly serializes the compiler-generated C# state machine iterator state and the workflow container's fields to JSON, bypassing replay entirely.
*   **Stateless Compute (The Runner):** The `Workflows.Runner` is completely stateless and has no I/O dependencies. It receives an execution request, hydrates the state machine in memory, advances it to the next step, dehydrates it, and returns the result.

---

## 2. Hybrid Persistence Model

To support rapid, sub-millisecond signal routing without performing expensive full table scans on large JSON fields, the engine uses a **Hybrid Document-Relational Persistence** model:

### A. The Aggregate Root (`WorkflowInstances` Table)
Stores the complete snapshot of the workflow run as the single source of truth.
*   `Id` (Guid, Primary Key): Unique run identifier.
*   `Status` (Enum): `Suspended`, `Completed`, or `Faulted`.
*   `StateObject` (JSON/JSONB): The serialized `WorkflowStateObject` containing the container class properties and C# state machine index.

### B. The Indexing Tables (`SignalWaits`, `CommandWaits`, `TimeWaits`)
Flat, relational tables dedicated solely to index active wait states for incoming signals or schedulers.
*   `SignalWaits`: Indexes active event handlers (webhooks, signals) with exact match filters so the database can route incoming signals to the correct instance in a single query.
*   `CommandWaits`: Indexes active out-of-process commands to correlate external responses.
*   `TimeWaits`: Indexes pending delay steps with execution timestamps for scheduler polling.

---

## 3. The Stateless Runner (Compute Brain)

The `Runner` processes execution requests through a two-phase pipeline:

```
                  ┌───────────────────────────────┐
                  │ 1. Hydrate Execution Context  │
                  └───────────────┬───────────────┘
                                  │
                                  ▼
                  ┌───────────────────────────────┐
                  │ 2. Evaluate Wait Matchers     │
                  └───────────────┬───────────────┘
                                  │ (Matches)
                                  ▼
                  ┌───────────────────────────────┐
                  │ 3. Loop: Advance State Machine│
                  └───────────────┬───────────────┘
                                  ├──────────────────────┐
                                  ▼ (Active Wait)        ▼ (Passive Wait)
                     ┌──────────────────────────┐   ┌──────────────────────────┐
                     │ Execute Sync in Memory   │   │ Map DTOs & Suspend Loop  │
                     └────────────┬─────────────┘   └────────────┬─────────────┘
                                  ▲                              │
                                  └──────────────────────────────┘
                                  │
                                  ▼
                  ┌───────────────────────────────┐
                  │ 4. Snapshot & Return state    │
                  └───────────────────────────────┘
```

1.  **Hydrate:** When an execution request arrives, the runner recreates the workflow container instance and the compiler-generated `IAsyncEnumerator<Wait>` iterator. Using **`FastExpressionCompiler`**, it compiles high-performance delegates to inject the saved database state and container properties back into the C# state machine fields.
2.  **Evaluate:** The runner uses in-memory matchers (e.g., `SignalWaitMatcher`, `GroupWaitMatcher`) to check if the incoming trigger satisfies the wait condition.
3.  **Advance Loop:** If matched, the runner loops and advances the state machine (`MoveNextAsync()`). It processes:
    *   **Active waits** (like immediate command execution or compensation registrations) synchronously in RAM.
    *   **Passive waits** (like waiting for a new external signal or delay) by pausing the loop, mapping new relational DB wait records, and suspending.
4.  **Dehydrate & Return:** The updated state is snapshotted to a DTO and sent back to the persistence layer.

---

## 4. The Single Transaction Cycle

When a signal arrives, the hosting environment executes these steps in a single, atomic database transaction:
1.  **Route:** Queries `SignalWaits` using standard SQL indexes to find the matching `WorkflowInstanceId`.
2.  **Load:** Fetches the workflow instance row and deserializes the JSON state.
3.  **Compute:** Dispatches the context and signal payload to the stateless runner.
4.  **Save:** Overwrites the JSON snapshot in `WorkflowInstances`, deletes completed wait indexes, and inserts newly yielded wait rows.
