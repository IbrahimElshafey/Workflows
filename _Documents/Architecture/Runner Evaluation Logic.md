# 🏗️ Workflows Engine: Runner Evaluation Logic

## Overview

The Workflows engine uses a **100% stateless** execution model. The **Runner** acts purely as a logical evaluation unit. When execution resumes, it operates on a fully hydrated execution context (`WorkflowExecutionContext`) compiled from the serialized snapshot (`WorkflowStateDto`) sent by the Orchestrator.

The Runner's lifecycle is structured as a **Two-Phase Pipeline**:
1. **Incoming Phase (Matchers)**: Validates incoming events (signals, timer triggers, command results) against the pending wait conditions.
2. **Execution Cycle (Processors)**: Advances the C# compiler-generated state machine and prepares newly yielded wait points.

Logically, the Runner sits in a fast-forward execution loop, deciding: **"Do I execute this instruction immediately and continue, or do I pause, serialize the state, and hand it back to the Orchestrator?"**

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

## 2. The Execution Phase: Processors

Once the matching phase completes successfully, the Runner loops, calling `StateMachineAdvancer.RunAsync()` to advance the state machine to the next `yield return`. The returned `Wait` object is routed through the `ProcessorFactory` to resolve the corresponding `WorkflowWaitProcessor`:

### Active Processors (Loop Continues)
Active operations do not pause execution. They run synchronously in-memory, mutating the context, and return `true` to instruct the Runner loop to advance immediately to the next C# instruction.

* **`ImmediateCommandProcessor`**: Resolves the command handler, executes the command, records the execution in the saga history, and invokes the `OnResultAction` or `OnFailureAction` callback.
* **`CompensationProcessor`**: Triggered when a workflow yields a compensation command (e.g. `Compensate("TokenA")`). Because the Runner holds the full history of executed commands in the state snapshot, it retrieves all commands matching `"TokenA"` and executes their registered compensation delegates in LIFO (Last-In, First-Out) order in RAM.

### Passive Processors (Loop Suspends)
Passive processors prepare the environment for external event subscriptions, map wait DTOs with routing keys, and return `false` to suspend the Runner loop.

* **`SignalWaitProcessor`**: Extracts the match expression, compiles the match delegates, updates template caches, and registers wait details to be indexed by the Orchestrator.
* **`TimeWaitProcessor`**: Calculates the absolute target datetime offset based on delay durations or targets, creating routing DTOs for the Orchestrator to register with its scheduler.
* **`DeferredCommandProcessor`**: Prepares out-of-process commands for dispatch by serializing payloads and generating keys so the Orchestrator can publish the command to an external bus.
* **`GroupWaitProcessor`**: Unfolds nested composite layers, validating that children contain only passive waits. It populates parent-child DTO relationships for relational index mapping.
* **`SubWorkflowProcessor`**: Initializes a new child execution context, advances it to its first wait point, and registers the child wait under the parent sub-workflow scope.

---

## 3. Cancellation & Pruning (`CancelProcessor`)

Cancellation is integrated directly into the runner's execution cycle to handle timeouts, business aborts, or alternate execution branches:

* **Token Registration**: When a workflow calls `CancelToken("TokenName")`, the token is added to the execution history.
* **Fast-Forward Check (`CheckAndSkipCancelledWaitAsync`)**: Before processing any yielded wait, the Runner checks if its cancel tokens intersect with the cancellation history. If so, it invokes the wait's `OnCanceled` callback, cancels the wait, and skips directly to the next state machine statement without suspending.
* **Sub-Tree Pruning**: When a branch finishes (e.g. in `MatchAny` groups), the `CancelProcessor` recursively finds all incomplete sibling waits and prunes them from active tables, ensuring no stale signals are routed to them in the future.

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
         b. If yielded wait is cancelled -> Run OnCanceled callback, skip, and loop again
         c. Resolve Processor (Active vs Passive)
         d. Run Processor:
             - Active (Command/Compensate) -> Execute in RAM, set loop = true
             - Passive (Signal/Time/Group/SubWorkflow) -> Map DTOs, set loop = false
         e. Process pending cancellations & prune database indexes
     }
4. Snapshot and Return (Map final state/DTOs and dispatch back to Orchestrator)
```
