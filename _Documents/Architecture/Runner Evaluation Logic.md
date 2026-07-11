# 🏗️ Workflows Engine: Runner Evaluation Logic

## Overview

The Workflows engine uses a **100% stateless** execution model. The **Runner** acts purely as a logical evaluation unit. When execution resumes, it operates on a fully hydrated execution context (`WorkflowExecutionContext`) compiled from the serialized snapshot (`WorkflowStateDto`) sent by the Orchestrator.

The Runner's lifecycle is structured as a **Two-Phase Pipeline**:
1. **Incoming Phase (Matchers)**: Validates incoming events (signals, timer triggers, command results) against the pending wait conditions.
2. **Execution Cycle (Serializers)**: Advances the C# compiler-generated state machine and prepares newly yielded wait points.

All yielded waits suspend execution of the C# state machine run loop. However, wait serializers can flag whether the instance context should be preserved in the memory cache (`KeepInCache = true`) or unloaded from the cache (`KeepInCache = false`).

---

## 1. The Incoming Phase: Matchers

When an external trigger arrives, the Orchestrator loads the workflow instance's state and sends a `WorkflowExecutionRequest` containing the triggering wait ID. The Runner resolves the corresponding `WorkflowWaitMatcher` from the `MatcherFactory`:

* **`SignalWaitMatcher`**: Evaluates the compiled expression tree (e.g. `.MatchExact(...)` and `.Where(...)`) in RAM against the incoming signal data. If the condition matches, the wait is marked `Completed`.
* **`TimeWaitMatcher`**: Timers are treated as external events scheduled by the Orchestrator. When they fire, this matcher marks the timer wait as `Completed`.
* **`DeferredCommandMatcher`**: Handles incoming async command results. If the execution succeeded, it routes the payload to `OnResultAction`. If an exception is returned, it routes to `OnFailureAction`.
* **`GroupWaitMatcher`**: Evaluates compound conditions (`MatchAll`, `MatchAny`, custom `MatchIf`). It aggregates statuses of child waits recursively:
  - `MatchAll`: Matches only when all child waits are `Completed`.
  - `MatchAny` / `MatchFirst`: Matches when any single child completes. Triggering this causes downward pruning, marking sibling waits as cancelled.
  - `MatchIf` (Custom Expression): Evaluates a custom delegate. If it returns true, the group completes and siblings are pruned.
* **`SubWorkflowWaitMatcher`**: A special recursive matcher. It loads the child workflow's state machine stream, advances it using `StateMachineAdvancer`, and cascades execution. If the child completes, the matcher marks the sub-workflow wait as `Completed` and continues upward.

---

## 2. The Execution Phase: Serializers (`WaitSerializer`)

Once the matching phase completes successfully, the Runner loops, calling `StateMachineAdvancer.RunAsync()` to advance the state machine to the next `yield return`. The returned `Wait` object is resolved to a corresponding `WaitSerializer` subclass:

* **`CommandSerializer`**: Handles command yields. It serializes the command contract into the wait DTO structure. If the command's execution mode is `Immediate`, it returns `true` for `KeepInCache` so the orchestrator runs it and resumes immediately. If `Deferred`, it returns `false`.
* **`CompensationWaitSerializer`**: Handles compensation waits. It suspends execution (`ContinueExecutionLoop = false`, `KeepInCache = false`) and maps a `CompensationWaitDto` so the Orchestrator/Saga worker can execute registered rollback handlers sequentially.
* **`SignalWaitSerializer`**: Extracts the match expression, compiles match delegates, updates template caches, and prepares wait details to be indexed by the Orchestrator. Returns `false` for `KeepInCache`.
* **`TimeWaitSerializer`**: Calculates the absolute target datetime offset based on delay durations or targets, creating routing DTOs for the Orchestrator to register with its scheduler. Returns `false` for `KeepInCache`.
* **`GroupWaitSerializer`**: Unfolds nested composite layers, validating that children contain only passive waits. It populates parent-child DTO relationships for relational index mapping. Returns `false` for `KeepInCache`.

---

## 3. Cancellation & Pruning (`CancelProcessor`)

Cancellation is integrated directly into the runner's execution cycle to handle timeouts, business aborts, or alternate execution branches:

* **Token Registration**: When a workflow calls `CancelToken("TokenName")`, the token is added to the execution history.
* **Fast-Forward Check (`CheckAndSkipCancelledWaitAsync`)**: Before processing any yielded wait, the Runner checks if its cancel tokens intersect with the cancellation history. If so, the Runner marks the wait as `Canceled` and fast-forwards directly to the next state machine statement without suspending.
* **Outbox Delegation**: The actual invocation of the `OnCanceled` callback delegate is handled asynchronously by the Orchestrator using the database outbox, rather than execution within the Runner.
* **Sub-Tree Pruning**: When a branch finishes (e.g. in `MatchAny` groups), the Orchestrator recursively finds all incomplete sibling waits and prunes them from active tables, ensuring no stale signals are routed to them in the future.

---

## The Runner's "Single Tick" Lifecycle

When a request arrives, the Runner runs a single compute tick:

```
1. Hydrate Execution Context (Populate state, closures, and local variables)
2. Run Matcher (Evaluate incoming event -> update matching wait statuses)
3. If matches successfully:
     while (ContinueExecutionLoop)
     {
         a. Advance C# State Machine (MoveNextAsync)
         b. If yielded wait is cancelled -> Mark as Canceled, skip, and loop again
         c. Resolve Serializer (WaitSerializer)
         d. Run Serializer:
             - Maps wait to DTO
             - Sets KeepInCache based on wait characteristics (e.g. True for Immediate Commands)
             - Sets ContinueExecutionLoop = false (suspends execution loop)
         e. Prune database indexes and handle cancellations
     }
4. Snapshot and Return (Map final state/DTOs, return KeepInCache, and dispatch back to Orchestrator)
```
