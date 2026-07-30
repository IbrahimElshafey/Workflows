# 🗺️ Workflows Engine: Master Feature Status & Priority Roadmap

This document serves as the single master source of truth tracking the feature status, architecture safeguards, and prioritized implementation roadmap for the Workflows engine codebase.

**Last reviewed:** 2026-07-30 | **Test suite:** 147 unit tests, all passing ✅

---

## 🏛️ Master Feature Dashboard

| # | Feature | Status | Implementation Details / Commit | Reference Spec |
|---|---------|--------|----------------------------------|----------------|
| **1.1** | Distributed Timer Scheduling (`TimeWait`) | 🟡 Partial | `Scheduler.cs` & `TimeWaitEntity` implemented; native DB `SKIP LOCKED` poller planned | [Timer Scheduling](file:///d:/MySrc/Workflows/_Documents/Architecture/Orchestrator-Side%20Distributed%20Timer%20Scheduling.md) |
| **1.2** | Side-by-Side (SxS) Version Routing | ✅ Implemented | `014eafc` (Out-of-process worker supervision & ALC compiled project routing) | [SxS Architecture](file:///d:/MySrc/Workflows/_Documents/Architecture/Workflow%20Side-by-Side%20(SxS)%20Execution%20Architecture.md) |
| **1.3** | Composite Wait Group & Pruning (`GroupWait`) | ✅ Implemented | Runner + Store (Recursive downward pruning of non-completed siblings) | — |
| **1.4** | Optimistic Concurrency & ConcurrencyToken | ✅ Implemented | Mapped `ConcurrencyToken` in EF Core; verified in `ConcurrencyAndSoftDeleteTests.cs` | — |
| **1.5** | Massive Fan-Out (WaitMany / WaitAny) | 🟡 Partial | External state DB table (`ExternalChildWaits`); integration tests pending | — |
| **1.6** | Background DB Pruning Worker | 🟡 Partial | Stale locks expired by worker; dead-row sweeper planned | — |
| **1.7** | High-Performance JSON Serialization | 🟡 Partial | Settings cached; direct stream I/O and `ArrayPool<char>` planned | — |
| **1.8** | Roslyn Analyzer — Attribute Verification | 🟡 Partial | AST closure/yield validation done; needs `[Workflow]` check | — |
| **1.9** | UI Administration Dashboard | ✅ Implemented | `f00364f` (ASP.NET Core MVC statistics, wait trees, and trace views) | — |
| **1.10**| Payload Monitoring & Zombie Recovery | 🔴 Not Started | Size thresholds warning, retry limit circuit breaker policies | — |
| **2.1** | Distributed Lock Registry | ✅ Implemented | `InstanceLockManager.cs` (Row-level SQL lock manager with TTL expiry worker) | [Lock Registry](file:///d:/MySrc/Workflows/_Documents/Architecture/Distributed%20Lock%20Registry.md) |
| **2.2** | Idempotent Signal/Command Processing | ✅ Implemented | `SignalInboxEntity` database deduplication table & atomic transaction check | [Idempotent Processing](file:///d:/MySrc/Workflows/_Documents/Architecture/Idempotent%20Signal%20Processing.md) |
| **2.3** | Poison Message & Dead Letter Queue (DLQ) | 🔴 Not Started | Needs retry count tracking, dead-letter storage, error isolation | [Poison & DLQ Spec](file:///d:/MySrc/Workflows/_Documents/Architecture/Poison%20Message%20&%20DLQ%20Handling.md) |
| **2.4** | State Payload Encryption at Rest | 🔴 Not Started | Needs `IStateEncryptor` wrapper (AES-256-GCM envelope security) | [Encryption Spec](file:///d:/MySrc/Workflows/_Documents/Architecture/State%20Payload%20Encryption.md) |
| **2.5** | OpenTelemetry Tracing | 🔴 Not Started | Needs `ActivitySource` instrumentation across orchestrator/runner | [Telemetry Spec](file:///d:/MySrc/Workflows/_Documents/Architecture/Telemetry%20&%20OpenTelemetry%20Integration.md) |
| **2.6** | Schema Compatibility Verification CLI (`dotnet-wf`) | ✅ Implemented | `dotnet-wf` CLI tool (`WorkflowDiffAnalyzer`, `DeploymentManifestGenerator`) | [Schema Spec](file:///d:/MySrc/Workflows/_Documents/Architecture/Workflow%20Schema%20Compatibility%20Verification.md) |
| **2.7** | Worker Supervision & DB Capability Routing | ✅ Implemented | `WorkerProcessSupervisor`, `WorkerCapabilityEntity`, and `WorkerDrainWorker` | [Out-of-Process Architecture](file:///d:/MySrc/Workflows/_Documents/Roadmap/Versioning/Out-of-Process%20Worker%20Supervisor%20Architecture.md) |
| **2.8** | Deterministic Outbox Command Idempotency | ✅ Implemented | [`CommandWaitId`](file:///d:/MySrc/Workflows/DataStore/Workflows.Storage.EntityFrameworkCore/OutboxMessageEntity.cs#L15) unique constraints & outbox deduplication | — |
| **2.9** | Runner Clustering & Work Partitioning | ❌ Deprecated | Replaced by single-unit embedded in-process hosting model | [Clustering Spec (Obsolete)](file:///d:/MySrc/Workflows/_Documents/Architecture/Runner%20Clustering%20&%20Work%20Partitioning.md) |

---

## 🚀 Prioritized Implementation Roadmap

### 🔴 High Priority (Core Reliability, Migration & Production Readiness)

#### 1. Native Distributed Timer Engine (DB `SKIP LOCKED` Poller)
* **Goal**: Enable fully reliable, distributed time-based triggers (`TimeWait`) without any external scheduler dependencies (Hangfire / Quartz).
* **Implementation Details**:
  - Implement a native DB polling query using `FOR UPDATE SKIP LOCKED` (PostgreSQL / SQL Server) or atomic row locking.
  - Implement a 2-tier timer engine: near-term timers (< 60s) scheduled in-memory via `System.Threading.Timer`, long-term timers polled from DB.
  - Cancel scheduled timer checks atomically when parent workflows or wait tokens are cancelled.

#### 2. Workflow Migration Instance Cancellation (`CancelInstance`)
* **Goal**: Provide a clean mechanism to cancel a workflow instance during migration if its state is unmigratable or deprecated.
* **Implementation Details**:
  - Add `CancelInstance(string reason)` helper method to the `WorkflowMigration` base class.
  - Implement `WorkflowMigrationCancelledException` to halt migration when invoked.
  - Intercept `WorkflowMigrationCancelledException` in `WorkflowMigrationExecutor` to transition the instance to `WorkflowInstanceStatus.Canceled` (status 400), append a `CancellationHistoryEntry`, and stop execution.

#### 3. Workflow Replacement & Respawn Migration Strategy (`CancelAndRespawn`)
* **Goal**: Enable replacing legacy V1 workflow instances with fresh V2 instances when internal state migration is unsafe or impractical.
* **Implementation Details**:
  - Support `MigrationStrategy.CancelAndRespawn` in `WorkflowMigration`.
  - Provide an async callback `OnMigrateAsync(oldInstance, runnerClient)` allowing developers to start a new V2 instance with mapped inputs.
  - Atomically cancel the V1 instance with an audit link pointing to the newly spawned V2 `WorkflowInstanceId`.

#### 4. Poison Message Retry Counters & DLQ Integration
* **Goal**: Quarantine repeatedly failing signals or crashing workflow instances.
* **Implementation Details**:
  - Implement a `FailureCount` counter in the `WorkflowInstances` schema.
  - Increment counter on unhandled exception and roll back state update.
  - If `FailureCount >= 3`, transition instance status to `InError` and route payload to a Dead Letter Queue (`workflows-dlq`).

---

### 🟡 Medium Priority (Security, Observability & Scale)

#### 5. Automated Pipeline Safety Matrix & SxS Enforcement
* **Goal**: Automatically enforce Side-by-Side (SxS) process execution whenever `HasBreakingChanges == true` and no explicit migration script is supplied.
* **Implementation Details**:
  - Integrate `WorkflowDiffAnalyzer` AST checks into the deployment engine.
  - Enforce fallback to isolated worker sub-processes if breaking structural changes are detected.

#### 6. State Payload Encryption at Rest (AES-256-GCM)
* **Goal**: Encrypt serialized JSON `StateObject` blobs in database rows to satisfy security policies.
* **Implementation Details**:
  - Implement symmetric **AES-256-GCM** encryption using Data Encryption Keys (DEKs) wrapped by a Key Management Service (KMS) master key.
  - Store `EncryptedDek` alongside the base64-encoded state payload in the database.
  - Implement EF Core `ValueConverter` for transparent encryption/decryption.

#### 7. Telemetry & OpenTelemetry Integration
* **Goal**: Provide standard distributed tracing and metric logging across services.
* **Implementation Details**:
  - Configure a dedicated `ActivitySource` (`Workflows.Engine`).
  - Inject W3C trace context headers into messaging queues and extract them in the Orchestrator.
  - Record execution durations, state size metrics, and workflow failure counters.

#### 8. DB Push Notification Outbox Dispatch (`LISTEN/NOTIFY`)
* **Goal**: Eliminate polling delays for outbox command dispatches.
* **Implementation Details**:
  - Implement push notification listeners (PostgreSQL `LISTEN/NOTIFY`, SQL Server CDC, or SQLite WAL change listeners).
  - Immediately dispatch commands on write events without polling intervals.

---

### 🔵 Low / Optimization Priority (High-Scale Performance & Cluster Partitioning)

#### 9. Runner Clustering & Work Partitioning (`RunnerLeases`)
* **Goal**: Scale out stateless Runner instances horizontally without execution race conditions.
* **Implementation Details**:
  - Create a SQL-backed `RunnerLeases` partition table.
  - Route execution request messages to runner nodes based on a CRC32 consistent hash of `WorkflowInstanceId`.

#### 10. High-Performance Zero-Copy IPC Transport (Protobuf / Shared Memory)
* **Goal**: Minimize IPC serialization overhead for ultra-large workflow state payloads (>5MB).
* **Implementation Details**:
  - Implement Protobuf / MessagePack or Shared Memory Ring Buffers (`System.IO.MemoryMappedFiles`) for local IPC.

---

## ⚠️ Architectural Safeguards & Edge Case Rules

### 1. Concurrent Mutation of Container-Level Collections
* **Engine Guarantee:** Sibling executions are *logically parallel but physically sequential*. All signal matches, callbacks, and state transitions are serialized through the Orchestrator using instance-level database locks ([`InstanceLockManager`](file:///d:/MySrc/Workflows/DataStore/Workflows.Storage.EntityFrameworkCore/InstanceLockManager.cs)).

### 2. Compensation & Cancellation Async Dispatch (Outbox-style)
* **How it works:** When a compensation or cancellation is requested, it is pushed to a background worker channel (`_channel.CompensationWriter` or `_channel.CancellationWriter`) and persisted. `CompensationWorker` and `CancelerWorker` process these requests asynchronously.

### 3. Worker Sub-Process Draining & OS Memory Reclamation
* **Implemented Mechanism:** Suspended instances can wait for weeks. To prevent resident process bloat under frequent deployments, [`WorkerDrainWorker`](file:///d:/MySrc/Workflows/Hosting/Workflows.Hosting.InProcess/WorkerDrainWorker.cs) monitors live DB active instance counts per `DllVersion` and automatically terminates worker processes (`ShutdownWorkerAsync`) when active instance counts reach 0.

### 4. Deep Recursive Schema-Drift (`WF301`) Verification
* **The Rule:** The Roslyn validator checks top-level properties of state POCOs, generic arguments of `.WithState<TState>()`, and nested object graphs against schema contracts to flag unsafe state shifts before publishing.
