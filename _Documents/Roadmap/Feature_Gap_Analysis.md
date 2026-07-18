# 📋 Workflows Engine: Gap Analysis & Feature Status

This document tracks the implementation status, planned roadmap, and key technical specifications for all required production features of the Workflows engine.

**Last reviewed:** 2026-07-13 | **Test suite:** 135 tests, all passing ✅

---

## 🏛️ Implementation Summary Dashboard

| # | Feature | Status | Commit / Details | Reference Spec |
|---|---------|--------|------------------|----------------|
| **1.1** | Distributed Timer Scheduling (`TimeWait`) | ✅ Implemented | `c88a148` (In-memory dict with Guid match keys) | [Timer Scheduling](file:///d:/MySrc/Workflows/_Documents/Architecture/Orchestrator-Side%20Distributed%20Timer%20Scheduling.md) |
| **1.2** | Side-by-Side (SxS) Version Routing | ✅ Implemented | `014eafc` (Archived project ALCs, in-process routing) | [SxS Architecture](file:///d:/MySrc/Workflows/_Documents/Architecture/Workflow%20Side-by-Side%20(SxS)%20Execution%20Architecture.md) |
| **1.3** | Composite Wait Group & Pruning (`GroupWait`) | ✅ Implemented | Runner + Store (Downward pruning of non-completed siblings) | — |
| **1.4** | Optimistic Concurrency Retry Pipeline | 🟡 Partial | Concurrency token mapped in EF; needs retry interceptor / loop | — |
| **1.5** | Massive Fan-Out (WaitMany / WaitAny) | 🟡 Partial | Implemented in `014eafc` via external state DB; needs integration tests | — |
| **1.6** | Background DB Pruning Worker | 🟡 Partial | Stale locks expired by worker; needs general dead-row sweeper | — |
| **1.7** | High-Performance JSON Serialization | 🟡 Partial | Settings cached; needs `ArrayPool<char>` and stream-based I/O | — |
| **1.8** | Roslyn Analyzer — Attribute Verification | 🟡 Partial | AST closure/yield validation done; needs `[Workflow]` check | — |
| **1.9** | UI Administration Dashboard | ✅ Implemented | `f00364f` (ASP.NET Core MVC statistics and wait trees views) | — |
| **1.10**| Payload Monitoring & Zombie Recovery | 🔴 Not Started | Size thresholds warning, retry limit circuit breaker policies | — |
| **2.1** | Distributed Lock Registry | ✅ Implemented | `014eafc` (Row-level SQL lock manager with TTL expiry worker) | [Lock Registry](file:///d:/MySrc/Workflows/_Documents/Architecture/Distributed%20Lock%20Registry.md) |
| **2.2** | Idempotent Signal/Command Processing | ✅ Implemented | `347a614` (SignalInbox database deduplication table) | [Idempotent Processing](file:///d:/MySrc/Workflows/_Documents/Architecture/Idempotent%20Signal%20Processing.md) |
| **2.3** | Poison Message & Dead Letter Queue (DLQ) | 🔴 Not Started | Needs retry count tracking, dead-letter storage, error isolation | [Poison & DLQ Spec](file:///d:/MySrc/Workflows/_Documents/Architecture/Poison%20Message%20&%20DLQ%20Handling.md) |
| **2.4** | State Payload Encryption at Rest | 🔴 Not Started | Needs `IStateEncryptor` wrapper (AES-256-GCM envelope security) | [Encryption Spec](file:///d:/MySrc/Workflows/_Documents/Architecture/State%20Payload%20Encryption.md) |
| **2.5** | OpenTelemetry Tracing | 🔴 Not Started | Needs `ActivitySource` instrumentation across orchestrator/runner | [Telemetry Spec](file:///d:/MySrc/Workflows/_Documents/Architecture/Telemetry%20&%20OpenTelemetry%20Integration.md) |
| **2.6** | Schema Compatibility Verification | 🔴 Not Started | Needs Roslyn-based CLI tool to analyze AST drift before deployment | [Schema Spec](file:///d:/MySrc/Workflows/_Documents/Architecture/Workflow%20Schema%20Compatibility%20Verification.md) |
| **2.7** | Runner Clustering & Work Partitioning | ❌ Deprecated | Replaced by single-unit embedded in-process hosting model | [Clustering Spec (Obsolete)](file:///d:/MySrc/Workflows/_Documents/Architecture/Runner%20Clustering%20&%20Work%20Partitioning.md) |

---

## 💡 Key Design Reminders & Edge Cases

*   **Parent Group Match Evaluation:** If `IsGenericMatchFullMatch == true` or `IsExactMatchFullMatch == true`, the Orchestrator should also evaluate the parent group match when its type is `WaitAll`. It should also change the wait status to `Matched`.
*   **Signal Routing Redirection:** A signal should not trigger multiple workflow instances of the same type simultaneously. However, if the first selected instance returns `Unmatched` after the runner completes the full evaluation, the system should sequentially evaluate the next candidate instance, and continue this process until a match is found or no instances remain (review `FindInstancesWaitingForSignalAsync`).
*   **State Machine Field Serialization:** We only serialize state machine generated class public fields except `<>1__state`, `<>2__current` and `<>4__this`.
*   **Auditing & Logging:** Link standard logs that happen when a specific workflow instance runs to this specific workflow instance.

---

## ⚠️ Outstanding Technical Concerns & Concurrency Safeguards

### 1. Concurrent Mutation of Container-Level Collections
*   **Problem:** Sibling waits executing in a parallel group (e.g. `WaitGroup`) often mutate container-level properties (e.g., writing results to a shared class `Dictionary`).
*   **Engine Guarantee:** Sibling executions are *logically parallel but physically sequential*. All signal matches, callbacks, and state transitions are serialized through the Orchestrator using instance-level database locks (`InstanceLockManager`). Sibling threads do not execute concurrently on the same workflow instance, making shared collection mutations safe from raw data races (assuming they don't spawn background tasks).

### 2. Compensation & Cancellation Async Dispatch (Outbox-style)
*   **How it works:** When a compensation or cancellation is requested, it is not executed immediately in-thread. Instead, the orchestrator records the request and pushes it to a background worker channel (`_channel.CompensationWriter` or `_channel.CancellationWriter`) and persists the status.
*   **Worker Execution:** The `CompensationWorker` and `CancelerWorker` process these requests asynchronously from the channel and sweep the DB, separating workflow execution from outbox messaging.
*   **Next Steps:** If a compensation delegate itself fails (e.g. refund error), the worker leaves the instance in `InError` status with the `CompensationWaitDto` stuck in `Waiting`. We must implement a terminal `CompensationFailed` status and dead-letter queue (DLQ) to notify human operators when automated compensation retries are exhausted.

### 3. Solution Plan: Deep Recursive Schema-Drift (`WF301`) Verification
*   **The Issue:** The current schema validator (`WF301`) only checks top-level properties of the state POCO. Nested classes, collections (like `List<ChapterInfo>`), and transient states passed via `.WithState()` are currently not recursively checked.
*   **Approved Solution Plan:**
    1.  **Recursive AST Extractor:** Extend `WorkflowSchemaExtractor.cs` to recursively walk the property types of the workflow container class and its state POCO. For any complex type (classes, records, structs) or generic collections, extract their internal public properties and map them inside `WorkflowName_Vxx_Schema.json`.
    2.  **Transient State Hook:** Extract generic type arguments of `.WithState<TState>()` calls and map their structures to the schema contract.
    3.  **Recursive Drift Analyzer:** Update the Roslyn analyzer (`VersioningRules.cs`) to recursively compare the compiled types of the archived class and their nested fields against the generated schema. Any type rename, narrowing, or removal inside nested structures will raise a fatal `WF301` compilation error.

### 4. ALC Memory Lifecycle under Long-Tail Suspension
*   **Problem:** Suspended instances can wait for human interaction for weeks. Keeping their archived ALCs resident in memory under frequent deploys will cause memory leaks.
*   **Planned Solution:** Transition the static assembly cache inside `WorkflowRegistry` to use a `WeakReference` or LRU cache model to enable .NET collectible-ALCs to unload completely when instances go to sleep.

### 5. Migration Code Safety & Scrutiny
*   **Constraint:** Hand-written migration classes (`WorkflowMigration`) are exempt from standard workflow safety rules (like `WF_ERR_UNSAFE_STATE` or `WF000`) because they do not implement async iterator `Run` methods. 
*   **Requirement:** Since the analyzer safety net does not apply to migration code, any migration script must receive high-scrutiny peer review to ensure state translation correctness.

### 6. Integration Testing State Resumption Semantics
*   **The Issue (Clarified):** Standard integration tests run workflows from start-to-finish in a single in-memory context. This hides serialization bugs (e.g. declaring a property that fails JSON serialization or using a property marked with `[JsonIgnore]` that is read after a resume point).
*   **Planned Solution:** Implement a test execution filter/harness that intercepts execution at *every* yield return, serializes the workflow state object to a JSON string, destroys the active runner instance, reconstructs a fresh runner from the DB/JSON state, and then resumes. This guarantees that the workflow is fully serializable and rehydratable at every single suspension point.

### 7. Recursive Sub-Workflow State Bloat (Anti-Pattern)
*   **The Issue:** Sub-workflow states are serialized inline within the parent's `StateObject.Locals` dictionary. Deep recursion (e.g. looping via `WaitSubWorkflow` repeatedly) causes state-tree nesting bloat, similar to `WaitGroup` scaling issues.
*   **Design Rule:** For high-count or unbounded loops, recursion via `WaitSubWorkflow` is an anti-pattern. Developers should instead write flat iterative loops (e.g., `while (!state.Approved)`) using properties on the explicit state POCO across suspension points.

### 8. SxS Version Lock Across Recursive Sub-Workflows
*   **The Guarantee:** When a sub-workflow is invoked, the engine resolves the container type and method metadata using `WorkflowState.WorkflowVersion` loaded from the database record. 
*   **Version Purity:** The entire recursive execution chain (root and all nested sub-workflows) runs within the ALC of that single instance version. Newly deployed versions are ignored, ensuring zero risk of version mixing or type drift mid-flight.

---

## 🔍 Detailed Feature Walkthrough

### 1. Planned but Unimplemented (or Partially Implemented) Features

#### 1.1. Distributed Timer Scheduling (`TimeWait`)
*   **Status:** ✅ **Implemented** — `c88a148`
*   **Details:** `Scheduler.cs` now accepts a stable `Guid timerId` (the `UniqueMatchId`) for each timer entry, enabling idempotent re-registration on restart. `WorkflowRunnerClient.cs` scans the committed state tree recursively for new `TimeWaitDto` entries and schedules them. Consumed timers are cancelled accordingly.
*   **Remaining Gap:** The in-memory `ConcurrentDictionary` scheduler is still not persisted across restarts. A database-backed recovery path (scanning the `TimeWaits` table on startup) is partially implemented in `Scheduler.StartAsync`, but the in-memory scheduler itself is not fully durable across process crashes.

#### 1.2. Side-by-Side (SxS) Version Routing
*   **Status:** ✅ **Implemented** — `014eafc`
*   **Details:** `WorkflowRegistry.cs` supports registering multiple versions of the same workflow name. `WorkflowVersionRouter` dispatches incoming execution requests to the correct version (loading the compiled legacy project `/Archive/{workflowName}/V{version}` DLL inside a collectible `AssemblyLoadContext` on a cold start) if no data migration exists.
*   **Remaining Gap:** Caching mechanism for assembly contexts uses static dictionary structures, leading to a collectible ALC retain/leak cycle (needs weak reference or weak cache management).

#### 1.3. Composite Wait Group Routing & Downward Pruning (`GroupWait`)
*   **Status:** ✅ **Implemented**
*   **Details:** `GroupCompletionChecker.PruneRemainingChildren` marks all non-completed sibling wait statuses as `Canceled` and adds their IDs to `ConsumedWaitsIds` for both `MatchAny` (`GroupWaitFirst`) and expression-based groups. Pruning is recursive and correctly propagates status updates through all indexing tables (SignalWaits, TimeWaits, CommandWaits) inside the same database transaction.

#### 1.4. Optimistic Concurrency Retry Pipeline
*   **Status:** 🟡 **Partially Implemented**
*   **Details:** EF Core `RowVersion` concurrency tokens are configured in `WorkflowsDbContext`, but the Orchestrator's signal processing loop does not catch `DbUpdateConcurrencyException` or retry with backoff.
*   **Planned Solution:** Wrap `WorkflowRunnerClient.SendWorkflowRunResultAsync` in a retry loop (3–5 attempts with jitter) catching `DbUpdateConcurrencyException`, re-fetching state, and reapplying the execution result.

#### 1.5. Massive Fan-Out (External State Pattern)
*   **Status:** 🟡 **Partially Implemented** (Needs integration tests)
*   **Details:** Supports `WaitMany` / `WaitAny` DSL methods. Composite waits are kept out of the inline JSON state blob (`[JsonIgnore]`) and saved instead to the relational `ExternalChildWaits` table (`ExternalChildWaitEntity`). The store re-hydrates child waits when loading instances and uses them during signal lookups.
*   **Critical Gap:** Missing integration tests. Current tests use a mock runner client which does not exercise the database write, re-hydrate, and prune cycles of the real SQLite store.

#### 1.6. Background Database Pruning Worker
*   **Status:** 🟡 **Partially Implemented**
*   **Details:** `LockExpiryWorker` runs periodically to clear expired locks. However, no database-wide orphan row sweeper exists for clearing out old `SignalWaits`, `TimeWaits`, `CommandWaits`, or `ExternalChildWaits` left behind after crashes.
*   **Planned Solution:** Add a `DbPruningWorker` that periodically sweeps and deletes wait rows whose parent `WorkflowInstance` is in `Completed` or `Faulted` states.

#### 1.7. High-Performance JSON Serialization
*   **Status:** 🟡 **Partially Implemented**
*   **Details:** Newtonsoft.Json settings are cached and reused, but character array pooling (`ArrayPool<char>`) and direct stream read/write paths are not integrated.
*   **Planned Solution:** Refactor `IObjectSerializer` implementations to accept a `Stream` parameter and use `JsonTextReader`/`JsonTextWriter` with array pooling.

#### 1.8. Roslyn Analyzer — Workflow Attribute Verification
*   **Status:** 🟡 **Partially Implemented**
*   **Details:** The compiler analyzer (`WorkflowAnalyzer.cs`) is written for closures and structural rules, but does not validate that all workflow container classes have a valid `[Workflow]` attribute.
*   **Planned Solution:** Add a rule in the analyzer that flags any class implementing `IAsyncEnumerable<Wait>` Run() without the `[Workflow]` attribute.

#### 1.9. UI Administration Dashboard
*   **Status:** ✅ **Implemented** — `f00364f`
*   **Details:** Added `Workflows.Admin.UI` project providing an ASP.NET Core MVC-based administration panel displaying dashboard metrics, workflow definition graphs, instance wait tree diagrams, and execution logs/trace views.

#### 1.10. Payload Monitoring & Zombie Recovery
*   **Status:** 🔴 **Not Started**
*   **Planned Solution:** Implement warning limits when JSON state sizes exceed safe thresholds and a retry-limit circuit breaker to suspend workflows that crash repeatedly.

---

### 2. Unplanned Production Must-Haves

#### 2.1. Distributed Lock Registry
*   **Status:** ✅ **Implemented** — `014eafc`
*   **Details:** SQL-backed lock manager (`InstanceLockManager.cs`) using `WorkflowInstance` row-level locking with `LockedUntil` timestamps. Stale locks are cleared by a background `LockExpiryWorker`.

#### 2.2. Idempotent Signal/Command Processing (Deduplication)
*   **Status:** ✅ **Implemented** — `347a614`
*   **Details:** Implemented `SignalInboxEntity` database inbox table. `Orchestrator.ProcessSignalAsync` checks the inbox for duplicate signal IDs before processing and records them upon completion to ensure once-only signal advancement.

#### 2.3. Poison Message & Dead Letter Queue (DLQ) Handling
*   **Status:** 🔴 **Not Started**
*   **Details:** Malformed signals will retry indefinitely in the channel-based ingress. Needs a DLQ table, retry counting in `WorkflowInstance`, and maximum attempt thresholds.

#### 2.4. State Payload Encryption
*   **Status:** 🔴 **Not Started**
*   **Details:** Serialized state is written as plain text JSON. Needs AES-256-GCM symmetric envelope encryption at rest before SQL writes.

#### 2.5. Telemetry & OpenTelemetry Integration
*   **Status:** 🔴 **Not Started**
*   **Details:** Needs `ActivitySource` distributed tracing instrumentation across orchestrator steps, runner processing, and store query boundaries.

#### 2.6. Schema Compatibility Verification
*   **Status:** 🔴 **Not Started**
*   **Details:** Needs a Roslyn AST parsing CLI tool that runs during CI/CD builds to compare new workflow layouts against reference schema contracts, preventing deployment of incompatible C# state machine index shifts.

#### 2.7. Runner Clustering & Work Partitioning
*   **Status:** ❌ **Deprecated**
*   **Details:** The original distributed multi-node runner clustering design using consistent hashing and SQL leases is deprecated. The system executes using the single-unit embedded in-process hosting model (`Workflows.Hosting.InProcess`) over memory-backed SQLite.
