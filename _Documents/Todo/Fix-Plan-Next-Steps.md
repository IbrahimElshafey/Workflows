# Fix plan — status and next steps

Companion to [Fix-Plan-Claude.md](Fix-Plan-Claude.md). This file records what was implemented in this pass and what remains, with the code locations verified against the repository.

## Status summary

| Phase | Scope | Status |
|---|---|---|
| 0 | CI target, hygiene, README claims | Done |
| 1 | Correctness safety net (rehydration harness, test split) | Done in this pass |
| 2 | Saga reliability (`CompensationFailed`, retries, DLQ, Admin retry) | Done in this pass |
| 3 | Performance (N+1 fixes, benchmarks, measured numbers) | Done in this pass |
| 4 | Versioning safety (deep WF301, drain, snapshots, grace window) | Done in this pass |
| 5 | Recursion guidance (`WF400`, `MaxSubWorkflowDepth`, docs, sample) | Done, plus code fix and Admin depth view in this pass |

## What changed in this pass

### Phase 0 — quick wins

- `.github/workflows/dotnet.yml`: `dotnet-version` is now `10.0.x`, matching the `net10.0` projects.
- `global.json`: pins SDK `10.0.100` with `rollForward: latestFeature`.
- `.gitignore`: ignores `/.jules/` and `/.qwen/`; both are removed from tracking.
- `delete-bins.ps1` moved to `scripts/delete-bins.ps1` (still cleans the repository root).
- `LICENSE`: copyright range is `2023-2026`.
- `README.md`: unmeasured "sub-millisecond" claims replaced with neutral wording.

### Phase 2 — saga reliability

- `WorkflowInstanceStatus.CompensationFailed` (`600`) separates "rollback exhausted" from `Canceled` and from `InError`.
- `CompensationWorker` retries with backoff via `CompensationWorkerOptions`, tracking attempts on `CompensationWaitDto`.
- Exhausted compensation publishes to the DLQ through `IDlqPublisher` (registered in DI, `NoOpDlqPublisher` remains the default) and stores the message via `WorkflowsDbContext.DlqMessages`.
- Admin UI exposes a "Stuck rollbacks" filter and an idempotent retry-compensation command.
- `_Documents/Design_Patterns/Workflow Compensation Logic (Saga Pattern).md` documents `OnFailure`, retry semantics, and the new terminal status.
- Tests: `Tests/Workflows.Runner.Tests/CompensationReliabilityTests.cs`.

### Phase 5 — recursion guidance

- `Workflows.Analyzers/Rules/SubWorkflowRules.cs`: `WF400` warning when a method passes itself to `WaitSubWorkflow`.
- `Workflows.Abstraction/Runner/WorkflowRunnerOptions.cs`: `MaxSubWorkflowDepth` (default `100`, `0` or negative disables). Registered with `TryAddSingleton` in `Workflows.Runner/DI.cs`.
- `WorkflowRunLoop.ResumeSubWorkflowAsync` increments/decrements the depth and throws `InvalidOperationException` naming `MaxSubWorkflowDepth` when exceeded.
- `Samples/WorkflowSample/ReviewAndReviseWorkflow.cs`: flat-loop review/revise sample.
- `_Documents/Design_Patterns/Sub-Workflow Execution.md`: "Recursion Guidance" section.
- Tests: `WF400` cases in `AnalyzerTests.cs`, `SubWorkflowDepthTests.cs` for the runtime guard.

### Phase 1 — correctness safety net

- `Workflows.Runner.TestShell` gained `RehydrationMode` (`Direct` / `Rehydrating`): after every `WorkflowRunResult` the harness serializes the state with the real serializer, discards the `ServiceProvider` (registry, runner, store) and rebuilds it from persisted state before resuming.
- `Tests/Workflows.Runner.Tests/WorkflowRehydrationTests.cs` runs every `TestWorkflows/*` workload in both modes (6 workloads × 2). `InMemoryTemplateRepository` was added because `AddWorkflowsRunner()` does not register `ITemplateRepository`.
- Found and fixed a real latent bug during this phase: `WorkflowHashCalculator.CalculateHash` ignored the workflow type, so two workflows sharing a `callerName` + identifier + lambda text shared one cached compiled delegate and threw `InvalidCastException` inside the signal matcher. Template keys are now scoped by workflow container type (`ScopeTemplateKey` in `Mapper.cs`).
- Negative tests: `SerializationHazardTests.cs` + `HostileSerializationWorkflows.cs` cover a `[JsonIgnore]` member read after a yield and an interface-typed member with no polymorphic setup; both fail loudly instead of resuming with default values.
- Analyzer gap closed: `WF010` (write to a `[JsonIgnore]` state member) and `WF011` (interface/abstract-typed state member the snapshot cannot reconstruct) in `SerializationRules.cs`, wired into `WorkflowAnalyzer` with tests.
- Test split: `Tests/TestSomething` deleted; new `Tests/Workflows.Storage.Tests` (SQLite always, Postgres/SQL Server joined through `StoreProviderMatrixTests` when `WORKFLOWS_TEST_POSTGRES` / `WORKFLOWS_TEST_SQLSERVER` hold a connection string) and `Tests/Workflows.OutOfProcess.Tests`. `.github/workflows/dotnet.yml` runs a three-way matrix.

### Phase 2 — follow-up worth confirming

- A missing `{HandlerKey}:Compensation` callback is no longer treated as a successful rollback: `CompensationWorker` collects the missing handlers and throws, so the attempt retries and the instance ends in `CompensationFailed` instead of `Canceled`.
- `IWorkflowStore.SaveContextSyncAsync` takes an optional `DlqMessage?`, so the terminal status change and the DLQ row commit in one transaction — the at-least-once window is gone. `DlqAtomicityTests` (3 tests) covers it.

### Phase 3 — performance

- N+1 fixes: the candidate first-wait probe in `SaveContextSyncAsync` is two set-based queries over a `HashSet<Guid>`; `IWorkflowStore.GetInstanceStatesAsync(IReadOnlyCollection<Guid>)` replaces the per-instance load in `ProcessSignalAsync`; `HydrateExternalChildWaitsAsync` hydrates from a single query through `ToLookup(ParentWaitId)`. `InstanceStateBulkLoadTests` (2 tests).
- `Benchmarks/Workflows.Benchmarks` (BenchmarkDotNet `ShortRun`): in-memory resume tick plus a signal → match → run → commit round trip on SQLite, and on Postgres when `WORKFLOWS_TEST_POSTGRES` is set via the new `AddWorkflowsInProcessHost(connectionString, registerStore)` overload.
- Removed hot-path `Console.WriteLine` debug output from `SignalCompletionChecker.cs` and `WorkflowStateService.cs`.
- `_Documents/Development/Performance Review.md` gained a "Measured performance" section naming the hardware, runtime, provider and measured numbers.
- CI: non-blocking `benchmarks` job (`continue-on-error`, 20-minute timeout).

### Phase 4 — versioning safety

- Deep `WF301`: `VersioningRules.CollectStateMemberNames` BFS-walks the archived state graph and `CollectWithStateTypes` reads `.WithState<T>()` generics through the semantic model. Three new tests.
- Drain counts `InError` instances holding a `Waiting` compensation wait as active (`GetActiveInstanceCountsByDllVersionAsync`).
- `WorkflowInstanceStatus.MigrationFailed` (`700`), a `WorkflowInstanceSnapshots` table and `SaveInstanceSnapshotAsync` / `GetInstanceSnapshotsAsync`; `WorkflowMigrationExecutor` snapshots before mutating and parks a failing instance on V1. `MigrationSnapshotTests` (2 tests).
- `WorkerDrainWorker` rebuilt around `WorkerDrainOptions` (`DrainingGracePeriod` = 2 minutes, parked-instance guard, `AllowShutdownWithParkedInstances`); `RestoreInstanceSnapshotAsync` and `GetParkedInstanceCountsByDllVersionAsync` provide the rollback path. The restore test caught a real bug: `ReplaceMigratedStateAsync` overwrote `WorkflowType` with the empty value from a partial snapshot.
- Instant Drop is gated: `--allow-instant-drop` in `Workflows.Tools.CLI` and `DeploymentManifestGenerator.InstantDropEnabled`. `InstantDropGateTests` (4 tests).
- Self-contained worker archive: `WorkerArchiveStore` (`ArchiveAsync`, `FindArchivedWorker`, `IsArchived`, `ListArchivedWorkers`, `WORKFLOWS_WORKER_ARCHIVE`), `WorkerProcessSupervisor.ResolveWorkerExecutable` / `TryArchiveWorkerAsync`, and archive-before-shutdown in the drain worker. `WorkerArchiveTests` (6 tests).

### Recursion extras

- `WF400` code fix ("Rewrite self-recursion as a flat loop") in `WorkflowCodeFixProvider`; offered only for single-parameter, tail-position self-calls, so the rewrite is behaviour-preserving. `SubWorkflowCodeFixTests` (3 tests).
- The Admin instance detail page reports sub-workflow depth against the configured limit: `InstanceDetailViewModel.SubWorkflowDepth` / `MaxSubWorkflowDepth`, computed from the persisted wait tree by `AdminQueryService.CalculateSubWorkflowDepth` and rendered in `Views/Instances/Detail.cshtml`. `AdminInstanceDetailDepthTests` (4 tests).

## Remaining follow-ups

Every item that was listed as a next step is implemented and covered by tests. What is still open is deliberately deferred:

- **Postgres / SQL Server in CI.** `StoreProviderMatrixTests` runs SQLite unconditionally and joins the other providers only when `WORKFLOWS_TEST_POSTGRES` / `WORKFLOWS_TEST_SQLSERVER` hold a connection string. No provider server is provisioned by the pipeline yet.
- **Benchmark history.** The `benchmarks` CI job is non-blocking and uploads nothing; add result storage once a baseline machine is fixed.
- **`WF400` code fix breadth.** Only single-parameter, tail-position self-calls are rewritten; non-tail recursion still needs a manual flatten (the analyzer warning still fires).
- **Snapshot retention.** `WorkflowInstanceSnapshots` grows with every migration; no retention worker exists.
- **Instant Drop default.** `--allow-instant-drop` is opt-in and should stay off until the grace window has soak time in production.
- **`Scheduler_ShouldFireBackgroundTimer_AndResumeWorkflow`** fails intermittently on a clean checkout; it is unrelated to these changes and still open.

## Verification notes

- `dotnet build Workflows.sln` — 0 errors, 36 warnings (the tree carries a large pre-existing warning count).
- `dotnet test Tests/Workflows.Runner.Tests/Workflows.Runner.Tests.csproj` — the only pre-existing failure is `OrchestrationIntegrationTests.Scheduler_ShouldFireBackgroundTimer_AndResumeWorkflow`; it fails on a clean checkout too.
- Suites: `Tests/Workflows.Runner.Tests` 176/177 (the single failure is `OrchestrationIntegrationTests.Scheduler_ShouldFireBackgroundTimer_AndResumeWorkflow`), `Tests/Workflows.Storage.Tests` 46/46 (provider cases for Postgres/SQL Server are skipped without a connection string), `Tests/Workflows.OutOfProcess.Tests` 3/3.
- Targeted runs used during this pass: `--filter "FullyQualifiedName~WF400|FullyQualifiedName~SubWorkflowDepth"`, `--filter "FullyQualifiedName~Compensation"`, `--filter "FullyQualifiedName~Rehydration|FullyQualifiedName~SerializationHazard"`, `--filter "FullyQualifiedName~WorkerArchive|FullyQualifiedName~WorkerCapabilityAndDrain|FullyQualifiedName~RestoreInstanceSnapshot"`, `--filter "FullyQualifiedName~InstantDropGate|FullyQualifiedName~SubWorkflowCodeFix|FullyQualifiedName~AdminInstanceDetailDepth"`.