# 📋 Workflows Engine: Gap Analysis & Feature Status

This document tracks the implementation status of all planned and required production features for the Workflows engine.

**Last reviewed:** 2026-07-12 | **Test suite:** 127 tests, all passing ✅

---

## Summary Dashboard

| # | Feature | Status | Commit |
|---|---------|--------|--------|
| 1.1 | Distributed Timer Scheduling | ✅ Implemented | `c88a148` |
| 1.2 | Side-by-Side Version Routing | ✅ Implemented | `014eafc` |
| 1.3 | Composite Wait Group / GroupWait Pruning | 🟡 Partial | — |
| 1.4 | Optimistic Concurrency Retry Pipeline | 🟡 Partial | — |
| 1.5 | Massive Fan-Out (WaitMany / WaitAny) | ✅ Implemented | `014eafc` |
| 1.6 | Background DB Pruning Worker | 🟡 Partial | — |
| 1.7 | High-Performance JSON Serialization | 🟡 Partial | — |
| 1.8 | Roslyn Analyzer — Attribute Verification | 🟡 Partial | — |
| 1.9 | UI Administration Dashboard | 🔴 Not Started | — |
| 2.1 | Distributed Lock Registry | ✅ Implemented | `014eafc` |
| 2.2 | Idempotent Signal/Command Processing | ✅ Implemented | `347a614` |
| 2.3 | Poison Message & Dead Letter Queue | 🔴 Not Started | — |
| 2.4 | State Payload Encryption at Rest | 🔴 Not Started | — |
| 2.5 | OpenTelemetry Tracing | 🔴 Not Started | — |
| 2.6 | Schema Compatibility Verification | 🔴 Not Started | — |

---

## 1. Planned Features

### 1.1. Distributed Timer Scheduling (`TimeWait`)
- **Status:** ✅ **Implemented** — `c88a148`
- **What was built:**
  - `Scheduler.cs` now accepts a stable `Guid timerId` (the `UniqueMatchId`) for each timer entry, enabling idempotent re-registration on restart
  - `WorkflowRunnerClient.cs` scans the committed state tree recursively (including inside `WaitGroupDto` children) for new `TimeWaitDto` entries and calls `Scheduler.ScheduleSignalAsync` using `UniqueMatchId` as the timer key
  - Consumed timers are cancelled by resolving their `UniqueMatchId` from the existing wait tree
  - `ChannelWorkflowRunnerClient.cs` now awaits `CoordinatorCommitWorker` acknowledgment before returning, eliminating a race condition where `GetInstanceStateAsync` returned null immediately after `StartWorkflowAsync`
  - `IExternalScheduler` interface updated accordingly
- **Remaining gap:** The in-memory `ConcurrentDictionary` is still not persisted across restarts. A database-backed recovery path (scanning `TimeWaits` table on startup) is partially implemented in `Scheduler.StartAsync`, but the in-memory scheduler itself is not durable across process crashes.

---

### 1.2. Side-by-Side (SxS) Version Routing
- **Status:** ✅ **Implemented** — `014eafc`
- **What was built:**
  - `WorkflowRegistry.cs` now supports registering multiple versions of the same workflow name
  - `WorkflowVersionRouter` dispatches incoming execution requests to the correct version based on the persisted instance's `WorkflowVersion` field
  - `SxSWorkflow` test exercises parallel v1/v2 routing
- **Remaining gap:** No database-backed `WorkflowRegistrations` table — registrations are still in-process memory only; process restart loses registration state.

---

### 1.3. Composite Wait Group Routing & Downward Pruning (`GroupWait`)
- **Status:** 🟡 **Partially Implemented**
- **The Issue:** `GroupWait.MatchAny()` correctly short-circuits at the runner level, but the Orchestrator does not cancel sibling timer waits or signal waits that become irrelevant once one branch fires. For example, in a `WaitGroup(delay, signal).MatchAny()`, once the `delay` fires, the registered `SignalWait` in the DB is not pruned and could still match future signals.
- **Next step:** Add a post-match pruning pass in `Orchestrator.ProcessSignalAsync` that cancels/removes sibling wait DB rows after a `MatchAny` group resolves.

---

### 1.4. Optimistic Concurrency Retry Pipeline
- **Status:** 🟡 **Partially Implemented**
- **The Issue:** EF Core `RowVersion` concurrency tokens are configured in `WorkflowsDbContext`, but the Orchestrator's signal processing loop does not catch `DbUpdateConcurrencyException` or retry with backoff.
- **Next step:** Wrap `WorkflowRunnerClient.SendWorkflowRunResultAsync` in a retry loop (3–5 attempts with jitter) catching `DbUpdateConcurrencyException`, re-fetching state, and reapplying the execution result.

---

### 1.5. Massive Fan-Out (External State Pattern)
- **Status:** ✅ **Implemented** — `014eafc`
- **What was built:**
  - New `WaitMany` / `WaitAny` DSL methods on `WorkflowContainer`
  - `ExternalGroupWait` / `ExternalGroupWaitDto` model a fan-out group persisted outside the JSON state blob
  - `ExternalChildWaits` relational table stores individual child wait metadata
  - `ExternalGroupWaitSerializer` handles serialization, DB persistence, and hydration of child wait rows
  - `GroupCompletionChecker` evaluates `WaitMany` (all children complete) vs `WaitAny` (first child fires, siblings pruned)
  - `WorkflowStore` updated with full `ExternalChildWaits` CRUD; `WaitFinder` routes signals to external group children
  - 14 new tests in `MassiveFanOutTests.cs`

---

### 1.6. Background Database Pruning Worker
- **Status:** 🟡 **Partially Implemented**
- **The Issue:** `LockExpiryWorker` was added to expire stale distributed locks, but no general-purpose orphan row sweeper exists for `SignalWaits`, `TimeWaits`, `CommandWaits`, or `ExternalChildWaits` left behind by hard crashes.
- **Next step:** Add a `DbPruningWorker` that periodically finds and deletes wait rows whose parent `WorkflowInstance` is in `Completed`/`Failed` state.

---

### 1.7. High-Performance JSON Serialization
- **Status:** 🟡 **Partially Implemented**
- **The Issue:** Newtonsoft.Json settings are cached and reused, but `ArrayPool<char>` pooling and stream-based read/write paths are not integrated.
- **Next step:** Refactor `IObjectSerializer` implementations to accept a `Stream` parameter and use `JsonTextReader`/`JsonTextWriter` with `ArrayPool<char>` backing.

---

### 1.8. Roslyn Analyzer — Workflow Attribute Verification
- **Status:** 🟡 **Partially Implemented**
- **The Issue:** `WorkflowAnalyzer.cs` validates async iterator correctness (closure captures, yield patterns) but does not enforce that workflow containers carry the `[Workflow("name", version)]` attribute.
- **Next step:** Add a new `DiagnosticDescriptor` rule in `WorkflowAnalyzer` that flags any class implementing `IAsyncEnumerable<Wait>` Run() without the `[Workflow]` attribute.

---

### 1.9. UI Administration Dashboard
- **Status:** 🔴 **Not Started**
- **Required:** A web UI for inspecting live workflow DAGs, viewing state blobs, manually retrying stuck instances, and viewing signal/command audit history.
- **Next step:** Design REST management API endpoints first, then build a React/Blazor dashboard over them.

---

## 2. Unplanned "Must-Haves" for Production

### 2.1. Distributed Lock Registry
- **Status:** ✅ **Implemented** — `014eafc`
- **What was built:**
  - `InstanceLockManager.cs` — SQL-backed distributed lock using `WorkflowInstance` row-level locking with a `LockedUntil` timestamp column
  - `LockExpiryWorker.cs` — background service that periodically expires stale locks (configurable interval)
  - `IInstanceLockManager` abstraction registered in DI
  - 18 tests in `InstanceLockManagerTests.cs` covering acquire, release, re-entrant, expiry, and concurrent scenarios

---

### 2.2. Idempotent Signal/Command Processing (Deduplication)
- **Status:** ✅ **Implemented** — `347a614`
- **What was built:**
  - `SignalInboxEntity` — EF Core entity with unique index on `SignalId`
  - `IWorkflowStore.HasSignalBeenProcessedAsync` / `MarkSignalAsProcessedAsync` — read/write deduplication gate
  - `Orchestrator.ProcessSignalAsync` checks inbox before processing; marks signal as processed after successful run
  - `WorkflowRunner` passes `TriggeringSignalId` through the execution pipeline for tracing
  - Tests added to `WorkflowStoreTests.cs` covering duplicate detection and replay scenarios

---

### 2.3. Poison Message & Dead Letter Queue (DLQ) Handling
- **Status:** 🔴 **Not Started**
- **Why:** A malformed or crash-inducing signal will retry indefinitely in the current channel-based ingress. No retry count tracking, no dead-letter sink.
- **Next step:** Add a `RetryCount` column to `WorkflowInstance`, a `DeadLetterQueue` table, and a configurable max-retry threshold in the `RunnerWorker`.

---

### 2.4. State Payload Encryption at Rest
- **Status:** 🔴 **Not Started**
- **Why:** The JSON state blob stored in `WorkflowInstances.StateObject` may contain PII or financial data. No field-level encryption is applied before persistence.
- **Next step:** Introduce an `IStateEncryptor` abstraction that wraps serialization with AES-256-GCM and stores the key reference alongside the ciphertext.

---

### 2.5. Telemetry & OpenTelemetry Integration
- **Status:** 🔴 **Not Started**
- **Why:** No distributed traces are emitted across orchestrator→runner→store hops, making production debugging rely entirely on logs.
- **Next step:** Instrument `Orchestrator`, `WorkflowRunner`, and `WorkflowStore` with `ActivitySource` spans; export to an OTLP endpoint.

---

### 2.6. Schema Compatibility Verification Tools
- **Status:** 🔴 **Not Started**
- **Why:** Changing a workflow's yield sequence (adding/removing steps, renaming variables) can silently break in-flight instance rehydration.
- **Next step:** Build a CLI tool that reads registered workflow ASTs (Roslyn), compares against serialized state schemas in the DB, and reports breaking changes before deploy.
