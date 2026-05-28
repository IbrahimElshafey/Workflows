# Feature Gap Analysis: Planned vs. Unimplemented & Unplanned Must-Haves

1. I want serialization to skip default values and `Array.Empty` instances to not show in JSON output.
6. If `IsGenericMatchFullMatch == true` or `IsExactMatchFullMatch == true`, the orchestrator should also evaluate the parent group match when its type is `WaitAll`. It should also change the wait status to `Matched`.
* Review method `FindInstancesWaitingForSignalAsync`
* Link standard logs that happen when a specific workflow instance runs to this specific workflow instance.
* A signal should not trigger multiple workflow instances of the same type simultaneously. However, if the first selected instance returns Unmatched after the runner completes the full evaluation, the system should sequentially evaluate the next candidate instance, and continue this process until a match is found or no instances remain.

---

## 1. Planned but Unimplemented (or Partially Implemented) Features

These features are documented across various roadmap files in this directory but have not yet been fully realized in the codebase.

### 1.1 Orchestrator-Side Distributed Timer Scheduling
* **Goal**: Enable fully reliable, distributed time-based triggers (`TimeWait`).
* **Current State**: **Unimplemented**. The orchestrator currently uses a basic, in-memory scheduler in [Scheduler.cs](file:///d:/MySrc/Workflows/Workflows.Orchestrator/Scheduler.cs) powered by an in-memory `ConcurrentDictionary` and a thread polling loop. This is fragile and will lose all scheduled timer signals if the application restarts.
* **Planned Solution**: Integrate Hangfire or Quartz.NET to serve as the distributed scheduler.

### 1.2 Group Wait Evaluation & Routing
* **Goal**: Handle complex evaluation, tracking of child counts, and downward branch pruning for composite `GroupWait` structures (e.g. `MatchAll` and `MatchAny`).
* **Current State**: **Partially Implemented**. The C# DSL models exist in [GroupWait.cs](file:///d:/MySrc/Workflows/Workflows.Definition/GroupWait.cs), but the Orchestrator doesn't support complex evaluation, tracking child counts, or early pruning.
* **Planned Solution**: Implement group evaluation logic in the Orchestrator.

### 1.3 Side-by-Side (SxS) Version Routing
* **Goal**: Run old, in-flight workflow instances on the exact version of the C# code they started with while routing new instances to the latest version.
* **Current State**: **Unimplemented**. Currently, [WorkflowRegistry.cs](file:///d:/MySrc/Workflows/Workflows.Runner/WorkflowRegistry.cs) stores registered workflows strictly by their Name, overwriting other versions in memory. When instantiating, the runner fetches the workflow container by name and ignores the version.
* **Planned Solution**: Scan assemblies for `[Workflow]` attributes, store routes in a `WorkflowRegistrations` database table, and resolve the exact version dynamically.

### 1.5 Optimistic Concurrency & ETag/RowVersion Retries
* **Goal**: Prevent state overwrites in high-throughput or clustered deployments by catching DB concurrency exceptions and retrying executions.
* **Current State**: **Partially Implemented**. The DB schema context in [WorkflowsDbContext.cs](file:///d:/MySrc/Workflows/DataStore/Workflows.Storage.EntityFrameworkCore/WorkflowsDbContext.cs) maps `ConcurrencyToken`, and the [WorkflowAuditingInterceptor.cs](file:///d:/MySrc/Workflows/DataStore/Workflows.Storage.EntityFrameworkCore/WorkflowAuditingInterceptor.cs) updates it automatically on updates. However, the Orchestrator and Runner do not catch concurrency exceptions or retry the execution pipeline with exponential backoff.
* **Planned Solution**: Implement catch-and-retry logic in the Orchestrator's pipeline.

### 1.6 Relational Database Pruning
* **Goal**: Periodically clean up orphaned wait records belonging to completed/faulted workflow instances to keep indexing fast.
* **Current State**: **Partially Implemented**. Active waits are pruned in [WorkflowStore.cs](file:///d:/MySrc/Workflows/DataStore/Workflows.Storage.EntityFrameworkCore/WorkflowStore.cs) when completed or cancelled via token, but no background pruning worker exists for database-wide cleanup of orphaned rows.
* **Planned Solution**: Add a background cleanup worker that periodically runs to prune dead/orphaned rows.

### 1.7 High-Performance JSON Serialization
* **Goal**: Minimize Garbage Collector (GC) pressure and avoid reflection in serialization loops.
* **Current State**: **Partially Implemented**. Newtonsoft.Json settings are utilized, but character array pooling (`ArrayPool<char>`), object reference loops preservation, and direct streaming to/from DB connections are not yet fully integrated.
* **Planned Solution**: Adopt array pooling, contract caching, and stream-direct serialization.

### 1.8 Massive Fan-Out (External State Pattern)
* **Goal**: Prevent JSON state machine bloat by storing massive fan-out conditions (e.g. waiting for 10,000+ items) out-of-process in a correlation DB table.
* **Current State**: **Unimplemented**. All composite waits (`GroupWait`) are stored inline in the instance state, causing state bloat on massive fan-outs.
* **Planned Solution**: Implement `WaitMany` or `WaitAny` that persist state out-of-process.

### 1.9 Payload Monitoring & Zombie Recovery
* **Goal**: Alert operators if workflow state sizes exceed safe thresholds and suspend workflows that enter infinite crash loops.
* **Current State**: **Unimplemented**.
* **Planned Solution**: Implement payload size logs/circuit breakers and retry limit policies.

### 1.10 Roslyn Compiler Attribute Validation
* **Goal**: Validate at compile-time that all workflow container classes have a valid `[Workflow]` attribute.
* **Current State**: **Unimplemented**. The compiler analyzer ([WorkflowAnalyzer.cs](file:///d:/MySrc/Workflows/Workflows.Analyzers/WorkflowAnalyzer.cs)) is written for closures and structural rules, but does not validate attributes.
* **Planned Solution**: Extend the Roslyn rules to include attribute validation checks.

### 1.11 UI Administration Dashboard
* **Goal**: A visual console displaying metrics, DAG diagrams, state inspectors, saga compensation paths, and manual lifecycle actions.
* **Current State**: **Unimplemented**. No UI projects or admin endpoints exist.

---

## 2. Unplanned but "Must-Have" Features for Production

These features are missing from both the current implementation and the existing roadmap documents, but are vital for a resilient production system.

### 2.1 Distributed Lock Registry (Inter-Instance Mutual Exclusion)
* **Why**: Separate workflow instances often need to modify or interact with the same external resource. A native way to acquire/release distributed locks (e.g., Redis or SQL-backed locks) within the workflow DSL is needed to prevent race conditions *across* instances.

### 2.2 Idempotent Signal/Command Processing (Deduplication)
* **Why**: Messaging infrastructures (like RabbitMQ, Kafka, or Service Bus) generally guarantee "at-least-once" delivery. Duplicate signals or command results could advance the workflow state twice or run duplicate side effects unless there is an out-of-the-box idempotency tracking log at the database layer.

### 2.3 Dead Letter Queue (DLQ) & Poison Message Handling
* **Why**: If a signal or command payload fails validation, throws during runner rehydration, or repeatedly errors on serialization, the system needs a way to move it to a DLQ so it doesn't block processing queues or crash the engine indefinitely.

### 2.4 State Payload Encryption at Rest
* **Why**: The serialized JSON state in the database frequently contains sensitive business data (PII, credentials, financial records). Under compliance mandates (GDPR, HIPAA, SOC 2), a production system must support encrypting this data before storing it.

### 2.5 Telemetry, Distributed Tracing & OpenTelemetry Integration
* **Why**: Debugging distributed systems requires trace context propagation. The system should natively emit OpenTelemetry span data so that incoming API requests, workflow execution cycles, runner dispatches, and command handler executions can be correlated in tools like Jaeger or Datadog.

### 2.6 Workflow Schema Compatibility Verification Tools
* **Why**: Developers deploying a new version of a workflow might inadvertently break compatibility with currently running instances (e.g. by removing or renaming wait points). A CLI tool or registration check is needed to analyze compile-time ASTs and ensure a new deployment doesn't break in-flight state rehydration.

### 2.7 Runner Instance Coordination & Work Partitioning (Clustering)
* **Why**: In a multi-node deployment, runners need to avoid resource contention. A partition strategy (e.g., consistent hashing on instance IDs) or leader election is required so that runner nodes process distinct subsets of active workflows without overloading the orchestrator or database.
