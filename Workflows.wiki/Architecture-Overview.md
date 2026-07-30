# Architecture Overview

The Workflows Engine is architected on a strict separation of concerns between **Domain Definition**, **I/O & Persistence**, **Compute & Execution**, and **Administration & Tooling**. While logically decoupled to maintain clean boundaries, they can be hosted in-process as a single unified unit (via `Workflows.Hosting.InProcess`) for zero-network overhead and simple monolith operations, or distributed across out-of-process worker sub-processes via Named Pipe IPC or gRPC.

---

## 1. Core Philosophy

* **Zero-Dependency DSL:** Workflow definitions reside in a pure standard library (`Workflows.Definition`) with no knowledge of databases, message brokers, or host setups.
* **No Replay Overhead:** Traditional workflow frameworks replay your code's entire history from event-sourcing logs to reconstruct local state variables. This engine directly serializes compiler-generated C# state machine iterator state and workflow container fields into JSON, bypassing replay entirely.
* **Stateless Compute (The Runner):** The `Workflows.Runner` is completely stateless and has no I/O dependencies. It receives an execution request, hydrates the state machine in memory, advances it to the next step, dehydrates it, and returns the result.
* **Out-of-Process Worker Supervision:** Isolated out-of-process worker execution managed by `WorkerProcessSupervisor` over Named Pipe IPC, enabling Side-by-Side (SxS) multi-version execution across independent AssemblyLoadContexts.

---

## 2. Hybrid Persistence Model

To support rapid, sub-millisecond signal routing without performing expensive full table scans on large JSON fields, the engine uses a **Hybrid Document-Relational Persistence** model:

```mermaid
graph LR
    subgraph Signal Routing (Relational Indexes)
        SW[SignalWaits Table]
        CW[CommandWaits Table]
        TW[TimeWaits Table]
    end

    subgraph Source of Truth (Document Store)
        WI[WorkflowInstances Table JSON / JSONB State]
    end

    Signal --> SW
    SW -->|Matched Instance ID| WI
```

### A. The Aggregate Root (`WorkflowInstances` Table)
Stores the complete snapshot of the workflow run as the single source of truth.
* `Id` (Guid, Primary Key): Unique run identifier.
* `Status` (Enum): `Running`, `Completed`, or `InError`.
* `StateObject` (JSON/JSONB): Serialized `WorkflowRunContext` containing container properties, C# state machine step index, and wait tree.

### B. The Indexing Tables (`SignalWaits`, `CommandWaits`, `CompensationWaits`)
Flat, relational tables dedicated solely to indexing active wait states for incoming signals, external commands, or schedulers.
* `SignalWaits`: Indexes active event handlers with exact match paths so the database can route incoming signals to the correct instance in a single indexed query.
* `CommandWaits`: Indexes active out-of-process commands to correlate external worker responses.
* `CompensationWaits`: Indexes Saga undo/rollback blocks for transaction compensations.

---

## 3. The Stateless Runner (Compute Brain)

The `Runner` processes execution requests through a multi-phase pipeline:

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

1. **Hydrate:** When an execution request arrives, the runner recreates the workflow container instance and the compiler-generated iterator state. Using **`FastExpressionCompiler`**, it compiles high-performance delegates to inject saved database state and container properties back into the C# state machine fields.
2. **Evaluate:** The runner uses in-memory matchers (e.g., `SignalWaitMatcher`, `GroupWaitMatcher`) to check if the incoming trigger satisfies the wait condition.
3. **Advance Loop:** If matched, the runner loops and advances the state machine (`MoveNextAsync()`). It processes:
   * **Active waits** (like immediate command execution or compensation registrations) synchronously in RAM.
   * **Passive waits** (like waiting for a new external signal or delay) by pausing the loop, mapping new relational DB wait records, and suspending.
4. **Dehydrate & Return:** The updated state is snapshotted to a DTO and sent back to the persistence layer.

---

## 4. Execution Channels & Worker Transport Architecture

The orchestrator and runner communicate via transport-agnostic message abstractions (`IMessageTransport`):

1. **In-Process Loopback Transport:** Uses high-throughput `System.Threading.Channels` (`WorkflowExecutionChannel`) for zero-network overhead in monolithic setups.
2. **Out-of-Process IPC Transport:** `WorkerProcessSupervisor` launches worker sub-processes, establishing Named Pipe IPC connections with custom JSON handshake protocol frames.
3. **Distributed Transport (gRPC / WebAPI):** `Workflows.Client.gRPC` and `Workflows.Client.WebApi` provide strongly-typed RPC channels and HTTP REST endpoints for cross-network node communication.

---

## 5. Admin Dashboard & Monitoring Architecture (`Workflows.Admin.UI`)

The embedded administration module connects directly to the storage provider:
- **Visual DAG Topology Viewer:** Renders workflow node transitions and dependency paths using `vis-network`.
- **Execution Trace Timeline:** Provides an aggregated event log of signals, commands, waits, delays, and state machine step progressions.
- **Instance Management:** Enables real-time variable inspection, manual signal triggering, instance cancellation, and termination.
