# Upcoming Implementation Tasks

This document contains a consolidated backlog of upcoming technical features and enhancements planned for the Workflows engine codebase.

## 1. Orchestrator-Side Distributed Timer Scheduling
* **Goal**: Enable fully reliable, distributed time-based triggers (`TimeWait`).
* **Implementation Details**:
  - Integrate **Hangfire** or **Quartz.NET** as the background scheduler in the Orchestrator.
  - When a Runner yields a `TimeWait` (e.g., Delay for 1 hour, or Wait Until a specific DateTime), the Orchestrator registers a callback job with the scheduler.
  - Upon job expiry, the scheduler triggers a background job that sends a `WorkflowExecutionRequest` containing the triggering wait ID back to the Runner to resume execution.
  - Implement scheduled job cancellation if the parent workflow or wait token gets cancelled before the timer expires.

## 2. Optimistic Concurrency & ETag / RowVersion Safeguards
* **Goal**: Prevent execution race conditions and state overwrites in high-throughput or clustered deployments.
* **Implementation Details**:
  - Add a `RowVersion` (for SQL Server) or `ConcurrencyToken` (PostgreSQL/SQLite) column to the `WorkflowInstances` persistence schema.
  - When loading the context, the Orchestrator reads the version token.
  - During the single transaction save cycle, the database update query must enforce:
    ```sql
    UPDATE WorkflowInstances 
    SET StateObject = @NewState, ConcurrencyToken = @NewToken 
    WHERE Id = @InstanceId AND ConcurrencyToken = @CurrentToken;
    ```
  - If zero rows are affected, throw a `ConcurrencyException`, trigger a transaction rollback, and retry the execution pipeline with an exponential backoff.

## 3. Relational Database Pruning for Completed/Cancelled Waits
* **Goal**: Keep the relational routing index tables (`SignalWaits`, `CommandWaits`) small and performant by pruning dead rows.
* **Implementation Details**:
  - When a workflow is cancelled, completed, or a branch is pruned (such as in a `MatchAny` GroupWait), the Runner identifies cancelled wait IDs.
  - The Orchestrator must intercept the execution result and execute a bulk delete on `SignalWaits` and `CommandWaits` matching those IDs.
  - Set up a background cleanup worker that periodically runs to prune orphaned wait records (e.g., records belonging to workflow instances that are already in `Completed` or `Faulted` status).

## 4. Static Code Quality & Compile-Time Roslyn Analyzers
* **Goal**: Stop developers from deploying bad code (such as variables captured in closures, which cannot be serialized).
* **Implementation Details**:
  - Extend `Workflows.Analyzers` with rules to enforce:
    - **No Captured Closures**: If a callback like `.MatchIf(x => x.Val == myLocalVar)` captures a local C# variable, raise a compiler warning/error. Developers must be forced to pass state explicitly via `.WithState(myLocalVar).MatchIf((x, state) => x.Val == state)`.
    - **Sealed Workflow Classes**: Throw a compiler error if a class inherits from `WorkflowContainer` but is not marked as `sealed` (which ensures predictable reflection scanning).
    - **Workflow Attribute Validation**: Ensure every workflow container has a `[Workflow(Name = "...", Version = "...")]` attribute to prevent configuration mismatch at runtime.
