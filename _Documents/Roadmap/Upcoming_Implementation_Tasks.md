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

## 4. Distributed Lock Registry
* **Goal**: Enable workflow instances to acquire and release exclusive locks relationally.
* **Implementation Details**:
  - Create the `DistributedLocks` and `LockWaiters` database tables.
  - Implement the `WaitLock(string lockKey, TimeSpan ttl)` and `ReleaseLock(string lockKey)` DSL wait methods in `Workflows.Definition`.
  - Update the Orchestrator to intercept lock requests, check ownership, register blocked waiters in a FIFO queue, and wake them up atomically when the lock holder releases the key.

## 5. Idempotent Signal & Command Result Processing
* **Goal**: Prevent duplicate signal processing due to at-least-once message broker deliveries.
* **Implementation Details**:
  - Create the `SignalInbox` database table.
  - Wrap incoming signal routing in a single SQL transaction that queries `SignalInbox` to detect duplicate `MessageId`s.
  - If a duplicate is detected, discard the message; otherwise, execute the runner, commit state changes, and save the processing log.
  - Set up a background clean-up job to prune older records outside the retention window (e.g. 7 days).

## 6. Poison Message Handling & Instance Suspension
* **Goal**: Quarantine malformed signals or repeatedly crashing workflow instances.
* **Implementation Details**:
  - Implement a `FailureCount` counter in the `WorkflowInstances` schema.
  - If a runner execution throws an unhandled exception, catch it, increment the counter, and roll back the state update.
  - If the counter reaches 3, transition the instance status to `Suspended` and route error details to a `WorkflowExecutionErrors` logging table.
  - Configure broker-level DLQ routing (`workflows-dlq`) for malformed request payloads.

## 7. State Payload Encryption at Rest
* **Goal**: Encrypt the serialized JSON `StateObject` blob in database rows to satisfy security policies.
* **Implementation Details**:
  - Implement symmetric **AES-256-GCM** encryption using local Data Encryption Keys (DEKs) wrapped by a Key Management Service (KMS) master key.
  - Store the `EncryptedDek` alongside the base64-encoded state payload in the database.
  - Implement an EF Core `ValueConverter` to handle encrypt/decrypt operations transparently during persistence.
  - Cache decrypted DEKs in memory to eliminate KMS network latency during consecutive execution ticks.

## 8. Telemetry & OpenTelemetry Integration
* **Goal**: Provide standard distributed tracing and metric logging.
* **Implementation Details**:
  - Configure a dedicated `ActivitySource` in the engine.
  - Inject W3C trace context headers into messaging queues and extract them in the Orchestrator to build end-to-end trace correlation spans.
  - Create child spans for database lookups, runner compute loops, and command handler executions.
  - Register gauges and counters for workflow starts, failures, execution durations, and state size limits.

## 9. Workflow Schema Compatibility Verification CLI Tool
* **Goal**: Enforce compile-time check constraints to prevent state rehydration exceptions.
* **Implementation Details**:
  - Create a new .NET CLI tool project (`dotnet-wf-verify`).
  - Use Roslyn Semantic Model AST analysis to parse and map the wait topologies of old vs. new assemblies.
  - Throw compile/build warnings or failures if an active wait point has been deleted or inserted in an existing path.

## 10. Runner Clustering & Work Partitioning
* **Goal**: Scale out stateless Runner instances horizontally without execution race conditions.
* **Implementation Details**:
  - Create a SQL-backed `RunnerLeases` partition table.
  - Have runner nodes acquire and renew partition leases dynamically on a background thread.
  - Route incoming execution request messages to runners based on a CRC32 consistent hash modulo of the `WorkflowInstanceId`.

