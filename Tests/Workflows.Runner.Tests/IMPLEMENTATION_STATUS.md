# Workflows Runner Test Suite - Status

**Last reviewed:** 2026-11-19 · **152 `[Fact]`/`[Theory]` methods** across 28 test classes in `Workflows.Runner.Tests`.

This file replaces the older hand-maintained "implementation status / test plan" notes, which drifted out of date as the engine shipped. The authoritative sources of truth are now:

* [`_Documents/Roadmap/ROADMAP.md`](../../_Documents/Roadmap/ROADMAP.md) — feature status dashboard.
* [`_Documents/Architecture/`](../../_Documents/Architecture/) — design documents.
* The tests themselves — behaviour contract.

---

## 1. How to run

```bash
dotnet test Tests/Workflows.Runner.Tests/Workflows.Runner.Tests.csproj
```

The project is part of `Workflows.sln` and targets `net10.0` (xUnit).

---

## 2. Test inventory

| Area | Test class | Tests |
| :--- | :--- | ---: |
| Signals, first wait, resume | `FirstWaitAndResumeTests` | 5 |
| Signal registration & cloning | `FirstWaitRegistrationAndCloningTests` | 3 |
| Group waits | `GroupWaitResumptionTests`, `NestedGroupsTests`, `GroupFilterTests` | 6 |
| Fan-out / parallel waits | `MassiveFanOutTests` | 2 |
| Sub-workflows | `SubWorkflowTests`, `SubWorkflowStatePersistenceTests`, `WaitAndSubWorkflowValidationTests` | 13 |
| Commands & compensation | `CompensationTests` | 3 |
| Cancellation | `CancellationTests` | 4 |
| Versioning & migration | `WorkflowVersioningTests`, `ComprehensiveVersioningTests`, `AdvancedMigrationTests`, `WorkflowAttributeTests` | 15 |
| Timers | `NativeDistributedTimerTests` | 3 |
| Worker routing & drain | `WorkerCapabilityAndDrainTests`, `OutofProcessWorkerTests` | 7 |
| Concurrency & locking | `InstanceLockManagerTests`, `ConcurrencyAndSoftDeleteTests` | 11 |
| Storage | `WorkflowStoreTests` | 7 |
| Serialization / expressions | `JsonObjectSerializerTests`, `ExpressionSerializerTests`, `MatchExpressionTransformerTests` | 30 |
| Orchestration end-to-end | `OrchestrationIntegrationTests`, `ClientIntegrationTests` | 10 |
| Test shell | `TestShellTests` | 5 |
| Analyzers | `AnalyzerTests` | 27 |

---

## 3. Coverage notes

* **Timers** — `NativeDistributedTimerTests` exercises the native DB-backed timer engine (`ClaimDueTimersAsync`, stale recovery), not a third-party scheduler.
* **Group waits** — `WaitAny`/`WaitMany` including nested groups and `MatchIf` filters are covered.
* **Migration** — both `MigrationStrategy.InPlace` and `MigrationStrategy.CancelAndRespawn` are covered.
* **Analyzers** — `AnalyzerTests` drives every `WF*` diagnostic (WF000, WF001, WF003–WF007, WF103, WF201–WF211, WF300, WF301, `WF_ERR_UNSAFE_STATE`) through `Roslyn` verification.

---

## 4. Known gaps

* No test project exists for `Workflows.Admin.UI`, `Workflows.Tools.CLI`, or the storage providers against a real SQL Server/PostgreSQL instance.
* Out-of-process worker tests use an in-memory transport, not a real cross-process broker.