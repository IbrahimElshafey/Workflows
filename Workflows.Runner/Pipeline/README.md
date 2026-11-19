# Runner Pipeline

The runner is split into a small pipeline of collaborating components. This file replaces the older "Phase 1 refactoring" design note, which described evaluator/handler interfaces (`IWorkflowWaitEvaluator`, `IWorkflowWaitHandler`, `IEvaluatorFactory`, `IHandlerFactory`) that were later consolidated into the components below.

## Execution context

**`Pipeline/WorkflowExecutionContext.cs`** — the mutable object threaded through one execution tick:

| Member | Meaning |
| :--- | :--- |
| `Signal` | The `SignalDto` that triggered this tick (null for command-result ticks). |
| `CommandResult` | The `CommandResultDto` payload that triggered this tick. |
| `WorkflowState` | The deserialized `WorkflowStateDto` for the instance. |
| `WorkflowInstance` | The live `WorkflowContainer` instance built by the hydrator. |
| `TriggeringWaitId` | Id of the wait that fired. |
| `WorkflowStream` | The `IAsyncEnumerable<WaitInfrastructureDto>` produced by advancing the state machine. |
| `ContinueExecutionLoop` | Whether to keep advancing after this tick. |
| `KeepInCache` | Whether the compiled instance should stay in the template cache. |
| `CurrentSubWorkflow` | Non-null while executing inside a sub-workflow. |
| `ConsumedWaitsIds` | Ids of waits satisfied by this tick. |

The tick loop itself lives in `WorkflowRunLoop.cs`; completion is decided by `Pipeline/CompletionChecker/WorkflowCompletionChecker.cs` plus the per-wait `*CompletionChecker` classes.

## Hydration

**`Pipeline/IWorkflowHydrator.cs` / `WorkflowHydrator.cs`** rebuild a `WorkflowContainer` from persisted state, and **`Pipeline/WorkflowStateService.cs`** creates the execution context from an incoming request.

## Waits

Wait types have a serializer (`Pipeline/Serializers/*Serializer.cs`, resolved by `SerializerFactory`) and a completion checker (`Pipeline/CompletionChecker/*.cs`, resolved by `CompletionCheckerFactory`):

* `SignalWait` — `SignalWaitSerializer`, `SignalCompletionChecker`
* `CommandWait` — `CommandSerializer`, `CommandCompletionChecker`
* `TimeWait` — `TimeWaitSerializer`, `TimeWaitMatcher`
* `GroupWait` / `ExternalGroupWait` — `GroupWaitSerializer` / `ExternalGroupWaitSerializer`, `GroupCompletionChecker`
* `CompensationWait` — `CompensationWaitSerializer`

## Related

* [`../README.md`](../README.md) — project overview and DI registration.
* [`../ExpressionTransformers/HowMatchExpressionTransformationWorks.md`](../ExpressionTransformers/HowMatchExpressionTransformationWorks.md) — `MatchIf` compilation pipeline.