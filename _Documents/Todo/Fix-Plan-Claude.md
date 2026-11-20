# Fix plan

Ordered so each phase protects the next. I can't push to your repo (read-only clone), but I can draft any phase as a patch, or as a PR if you attach it with push access.

## Phase 0: quick wins (about half a day)

| Item | Action | Done when |
|---|---|---|
| CI target | In `.github/workflows/dotnet.yml`, set `dotnet-version: 10.0.x`. Optionally add a `global.json`. | CI is green on a push to master. |
| Hygiene | Delete or gitignore `.jules/` and `.qwen/`. Move `TestSomething` into `Samples/` or delete it. Move `delete-bins.ps1` to `scripts/`. Update the LICENSE year to 2023–2026. | Repo root contains only intentional entries. |
| README claims | Replace "sub-millisecond execution ticks" with a neutral phrase until Phase 3 produces numbers. | No unmeasured performance claim remains. |

## Phase 1: correctness safety net (about 1 week)

**Every-yield rehydration harness.** Do this first, because it tests everything built after it.
- Add a decorator around the test runner client (`TestShellWorkflowRunnerClient` is the natural seam). After every `WorkflowRunResult` it serializes the context with the real serializer, discards the registry and runner, builds fresh ones, and resumes.
- Run the existing `TestWorkflows/*` under it as an xUnit theory (direct and rehydrating modes).
- Add negative tests: a `[JsonIgnore]` property read after a yield, and an interface-typed property with no polymorphic setup. Both must fail loudly.
- Check whether `SerializationRules.cs` already flags those two cases at compile time. If not, add the rules so the harness isn't the only line of defense.
- **Done when:** all existing workflow tests pass in both modes, and the negative tests fail with a clear message.

**Test project split.** Create `Workflows.Runner.Tests` (pure runner and analyzer), `Workflows.Storage.Tests` (SQLite, with a Postgres/SQL Server matrix via Testcontainers or CI services), and `Workflows.OutOfProcess.Tests`. Update CI to run them as a matrix. **Done when:** CI shows per-provider results.

## Phase 2: saga reliability (about 1 week)

**`CompensationFailed` and DLQ.**
1. Add `CompensationFailed` to `WorkflowInstanceStatus`.
2. In `CompensationWorker`, apply retry with backoff, configurable per delegate or globally. On exhaustion, set the status and publish to the DLQ through the existing `IDlqPublisher` (see `Poison Message & DLQ Handling.md`; `NoOpDlqPublisher` is the default today).
3. Add an Admin UI "Stuck Rollbacks" filter plus a manual "retry compensation" action.
4. Tests: a throwing delegate leads to `CompensationFailed` plus a DLQ message. Retry count is honored. Admin retry is idempotent. Mid-rollback is distinguishable from rolled-back.

**`OnFailure` docs.** Document it in `Workflow Compensation Logic (Saga Pattern).md` as the inline alert path, with a refund example. It already registers in `Mapper.cs`, so this is mostly documentation plus one test.

**Done when:** a refund that fails permanently ends in `CompensationFailed`, raises an alert, and can be retried by an operator.

## Phase 3: performance (about 1 week)

1. Fix the three severe N+1 items from `Performance Review.md`:
   - candidate matching in `SaveContextSyncAsync`
   - instance loading in `ProcessSignalAsync`
   - external-wait hydration
2. Add a `Workflows.Benchmarks` project using BenchmarkDotNet, with two benchmarks:
   - a pure Runner tick, in memory
   - a signal-to-commit round trip on SQLite and on Postgres
3. Publish the measured numbers in the README with the hardware and provider noted, and run the benchmarks as a non-blocking CI job.

**Done when:** every performance statement in the docs links to a benchmark result.

## Phase 4: versioning safety (about 2 weeks)

I could not confirm the first two items from code, so start by checking each one.

1. **Deep `WF301`.** Verify whether `WorkflowSchemaExtractor` and `VersioningRules` recurse into nested types and `.WithState<T>()` generics. If not, implement it as planned.
2. **Drain definition.** Confirm that "active" in the drain logic includes pending compensations. `WorkerCapabilityAndDrainTests` exist, so extend them rather than starting over. My grep for `HasPendingCompensation` found nothing.
3. **Pre-migration snapshots.** Add a `WorkflowInstanceSnapshots` table, written before each migration. Run each instance in its own savepoint. A failure leaves that instance on V1 with a `MigrationFailed` status.
4. **Grace window.** Add `DrainingGracePeriod` before terminating the old worker, with a snapshot rollback path.
5. **Self-contained workers.** Archive V-N workers as self-contained publishes so a late compensation can respawn one after the host's runtime moves on.
6. **Gate Instant Drop** behind a feature flag. It stays off until items 1 to 4 pass their tests.

## Phase 5: recursion guidance (about 2 days)

- Add a warning to `Sub-Workflow Execution.md`: bounded recursion is fine, unbounded review loops should be flat iterations over state POCO properties.
- Add a Roslyn warning, for example `WF4xx`, when a method passes itself to `WaitSubWorkflow` (self-recursion). It should be a warning, not an error.
- Add a configurable `MaxSubWorkflowDepth` that fails loudly instead of growing silently.
- Add a `ReviewAndRevise` sample in the flat-loop style, which is also a good reference for new users.

## Order and effort

| Phase | Effort | Depends on |
|---|---|---|
| 0 | S | none |
| 1 | M | 0 |
| 2 | M | 1 |
| 3 | M | 0 (parallel with 1 and 2) |
| 4 | L | 1 and 2 |
| 5 | S | none (can slot in anywhere) |

Phase 4 last, because Instant Drop is the one irreversible feature and it should only ship once the harness and deep drift checks exist. If you want a first PR, Phase 0 plus the Phase 5 docs are the quickest.