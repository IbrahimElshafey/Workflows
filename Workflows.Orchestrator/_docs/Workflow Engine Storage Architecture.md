# Workflow Engine Storage Architecture: Hybrid Document-Relational Model

This document outlines the persistence strategy for the Workflows engine. The architecture employs a **Hybrid Document-Relational** approach, optimizing for both high-performance state-machine execution and rapid signal routing.

## 1. Architectural Philosophy

The system distinguishes between two primary data needs:
1.  **State Persistence:** The complex, hierarchical state of a running workflow instance (local variables, closures, nested wait trees).
2.  **Routing & Matching:** The ability to find a specific workflow instance among millions when an external event (Signal) or command result occurs.

To solve this, we use a **"Document as Source of Truth, Tables as Routing Indexes"** pattern.

## 2. The Source of Truth: `WorkflowInstances` Table

The `WorkflowInstances` table serves as the Aggregate Root. Instead of mapping every C# class field to a relational column, the entire execution context is snapshot-serialized.

| Column | Data Type | Description |
| :--- | :--- | :--- |
| **Id** | Guid (PK) | Unique identifier for the specific workflow run. |
| **WorkflowRegistrationId** | Guid (FK) | Links to the immutable versioned workflow definition. |
| **Status** | Enum | current state: `Suspended`, `Completed`, `Faulted`. |
| **StateObject** | JSON / JSONB | **The Source of Truth.** Holds the serialized `WorkflowRunContext`. |

### What's inside `StateObject` (JSON)?
This column contains the complete `WorkflowRunContext`, which includes:
* **WorkflowContainer:** The developer's class instance with all local variables.
* **Internal Mechanics:** The C# state machine index and compiler-generated closures.
* **Recursive Wait Tree:** All waits tree.

## 3. The Routing Indexes: Signal and Command Tables

To avoid expensive "Full Table Scans" inside JSON blobs, specific relational tables are maintained for waits that require external matching.

### `SignalWaits` Table
Used to route incoming webhooks, events, and **TimeWaits**.
* **Id** (Guid)
* **WorkflowInstanceId** (FK)
* **Path / MatchId**: Indexed string used for exact-match lookups.
* **Status**: `Waiting`, `Completed`.

> **Note on TimeWaits:** Timers are treated as external signals. When a `TimeWait` is yielded, a row is added to this table, and an external scheduler is tasked with sending a "Signal" back to the engine at the expiration time.

### `CommandWaits` Table
Used to track asynchronous outgoing requests.
* **Id** (Guid)
* **WorkflowInstanceId** (FK)
* **CommandKey**: Used to match the incoming result to the correct call site.

## 4. Execution Workflow (The Persistence Cycle)

When a signal arrives, the Orchestrator performs the following atomic operation:

1.  **Route:** Query `SignalWaits` to find the `WorkflowInstanceId` via an indexed lookup.
2.  **Load:** Fetch the single row from `WorkflowInstances` and deserialize the `StateObject` JSON.
3.  **Execute:** Pass the hydrated context to the **Stateless Runner**.
4.  **Sync (Single Transaction):**
    * Update `WorkflowInstances.StateObject` with the new JSON snapshot.
    * Delete/Mark `Completed` any Signal/Command waits that were satisfied.
    * Insert new rows into `SignalWaits` or `CommandWaits` if the Runner yielded new external dependencies.

## 5. Benefits of this Design

* **No ORM Complexity:** Eliminates the need for Table-Per-Hierarchy (TPH) mapping or complex recursive SQL queries for group waits.
* **Performance:** Loading a workflow is a single `Key-Value` lookup.
* **Scalability:** Routing is handled by standard SQL indexes, allowing the system to handle millions of active waits efficiently.
* **Reliability:** The hybrid model maintains ACID compliance within the RDBMS, ensuring state and routing indexes never drift.