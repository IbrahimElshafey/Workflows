# Sub-Workflow Execution Implementation

## Overview

Sub-workflows (also known as "resumable functions" or "child workflows") allow workflows to be composed hierarchically. A parent workflow can invoke child workflows that have their own wait points, state management, and execution flow.

## Implementation Status: ✅ COMPLETE

The runner now fully supports sub-workflow context switching, recursive nesting, and unified state serialization.

---

## Architecture

### Key Components

1. **SubWorkflowWait** (`Workflows.Definition/SubWorkflowWait.cs`)
   - Represents a wait point that triggers child workflow execution.
   - Contains `Runner` property: the child's `IAsyncEnumerable<Wait>` enumerator.
   - Contains `FirstWait` property: the first wait yielded by the child.

2. **Unified State Management** (`WorkflowStateObject.Locals`)
   - Child workflow states (`WorkflowStateObject`) are stored inside the parent's `WorkflowStateObject.Locals` dictionary, keyed by a stable Guid (`subWorkflowWaitDto.StateMachineObjectId`).
   - This eliminates dedicated StateMachinesObject tables/columns and unifies all states into the JSON document representing the main instance.

3. **Core Run Loop Interception** (`WorkflowRunLoop.ExecuteAsync` / `ResumeSubWorkflowAsync`)
   - **No Duplicate Loops:** The previous `SubWorkflowProcessor` has been deleted. Instead, the core execution loop in `WorkflowRunLoop` directly intercepts any yielded `SubWorkflowWait` and executes/resumes the sub-workflow in-place using `ResumeSubWorkflowAsync`.
   - **Nesting of DTOs:** When inside a child execution context, any yielded wait DTO is automatically nested under the parent `SubWorkflowWaitDto.ChildWaits` collection rather than appended to the root waits list.
   - When the child completes, the runner automatically removes the child's local state, sets the sub-workflow wait status to `Completed`, and resumes the parent stream.

---

## Execution Flow

### Initial Sub-Workflow Start

```
1. Parent workflow yields: WaitSubWorkflow(ChildEnumerator(), "ChildName")
2. Runner detects SubWorkflowWait directly inside the execution loop
3. Runner maps it to SubWorkflowWaitDto and sets parent/nesting relationships
4. Runner calls ResumeSubWorkflowAsync:
   - Registers a new child WorkflowStateObject inside parent.Locals[childKey]
   - Evaluates child's parameters and invokes the child enumerator stream
   - Advances child stream using the unified ExecuteAsync loop
5. DTO hierarchy: SubWorkflowWaitDto.ChildWaits = [child's first yielded wait]
```

### Sub-Workflow Resumption

```
1. An incoming Signal or Command result matches the child wait
2. Runner matches the wait and notes its ParentWaitId is set
3. Runner loads the parent SubWorkflowWaitDto
4. Runner retrieves the child state from parent.Locals[childKey]
5. Runner calls ResumeSubWorkflowAsync to advance the child enumerator (not parent)
6. If child yields another wait: save child state in parent.Locals, suspend and return wait
7. If child completes: remove child state, mark sub-workflow completed, resume parent
```

### Sub-Workflow Completion

```
1. Child enumerator returns null (completed natively)
2. Runner removes child state: parent.Locals.Remove(childKey)
3. Runner updates subWorkflowWaitDto.Status = Completed
4. Runner re-hydrates parent stream and continues parent execution
```

---

## Code Example

### Parent Workflow

```csharp
public override async IAsyncEnumerable<Wait> ExecuteWorkflowAsync()
{
    ExecutionLog.Add("Parent: Start");

    // Wait for initial signal
    yield return WaitSignal<OrderSignal>("OrderReceived", "Initial")
        .AfterMatch((signal) => ExecutionLog.Add($"Parent: Order {signal.OrderId}"));

    // Execute sub-workflow
    yield return WaitSubWorkflow(
        ProcessOrderSubWorkflow(),  // Child enumerator
        "ProcessOrder",             // Sub-workflow name
        "Process order items");     // Description

    ExecutionLog.Add("Parent: Sub-workflow completed");

    // Another sub-workflow with state
    yield return WaitSubWorkflow(
        ShipmentSubWorkflow(),
        "Shipment",
        "Handle shipment")
        .WithState("ShipmentState");

    ExecutionLog.Add("Parent: End");
}
```

### Child Sub-Workflow

```csharp
private async IAsyncEnumerable<Wait> ProcessOrderSubWorkflow()
{
    ExecutionLog.Add("SubWorkflow: Start");

    // Sub-workflow can have its own waits
    yield return ExecuteCommand<ReserveInventoryCommand, ReserveInventoryResult>(
        "ReserveInventory",
        new ReserveInventoryCommand { ProductId = "PROD-1", Quantity = 1 })
        .OnResult((result) => ExecutionLog.Add($"SubWorkflow: Inventory reserved - {result.ReservationId}"));

    yield return WaitSignal<PaymentSignal>("PaymentConfirmed", "Payment wait")
        .AfterMatch((signal) => ExecutionLog.Add($"SubWorkflow: Payment confirmed - {signal.TransactionId}"));

    ExecutionLog.Add("SubWorkflow: End");
}
```

---

## State Persistence

### Unified State JSON Serialization

When serialized to the NoSQL document store, the parent state shows the child state machine object embedded inside `Locals`:

```json
{
  "StateIndex": 5,
  "Instance": { /* ParentWorkflow instance */ },
  "Locals": {
    "3fa85f64-5717-4562-b3fc-2c963f66afa6": {  // childKey (SubWorkflowWaitDto.StateMachineObjectId)
      "StateIndex": 2,
      "Instance": { /* Shared ParentWorkflow instance properties */ },
      "Locals": {
        "state": { /* Child local variables state if typed */ }
      }
    }
  }
}
```

---

## Recursion Guidance

Sub-workflows may call other sub-workflows, and **bounded** recursion is a legitimate pattern — for example a tree walk with a known maximum depth, or a retry chain with an explicit attempt counter.

**Unbounded recursion is not.** A review/revise cycle, a polling loop, or any "keep asking until the reviewer is happy" flow should be written as a flat iteration over a state property, not as a sub-workflow that calls itself:

```csharp
// Preferred: one wait, repeated by a loop. The snapshot stays a fixed size.
while (Revision <= MaxRevisions)
{
    yield return WaitSignal<ReviewFeedbackEvent>("ReviewFeedback", "Wait for reviewer feedback")
        .AfterMatch((ReviewFeedbackEvent feedback) =>
        {
            LatestFeedback = feedback.Comments;
            Approved = feedback.Approved;
        });

    if (Approved) yield break;
    Revision++;
}

// Avoid: every round nests another child state machine under Locals,
// so the serialized document grows with each revision.
yield return WaitSubWorkflow(ReviewRound(), "ReviewRound");
```

Why it matters: each nesting level stores another child `WorkflowStateObject` inside `StateObject.Locals` (see [State Persistence](#state-persistence)). Recursive review loops therefore grow the persisted document on every round until the instance payload becomes expensive to load and save.

### Guardrails

| Guard | Where | Behaviour |
|---|---|---|
| `WF400` analyzer warning | `Workflows.Analyzers` | Warns (does not fail the build) when a method passes itself to `WaitSubWorkflow`. Siblings and other sub-workflow methods are unaffected. |
| `WF400` code fix | `Workflows.Analyzers` | "Rewrite self-recursion as a flat loop". Only offered when the self-call is the last statement of the method (optionally followed by `yield break;`) and the method takes the state as its single parameter, so the rewrite is behaviour-preserving. |
| `MaxSubWorkflowDepth` | `WorkflowRunnerOptions` | Runtime guard. Defaults to `100`; set to `0` or a negative value to disable. Exceeding the limit throws `InvalidOperationException` naming the configured limit instead of silently growing the snapshot. |

Override the limit when registering the runner:

```csharp
services.AddWorkflowsRunner();
services.AddSingleton(new WorkflowRunnerOptions { MaxSubWorkflowDepth = 16 });
```

See `Samples/WorkflowSample/ReviewAndReviseWorkflow.cs` for a complete flat-loop implementation of a review/revise cycle.

### Observing depth

The Admin UI instance detail page shows **Sub-workflow depth** next to the configured limit. The depth is derived from the persisted wait tree — every `SubWorkflowWait` / `PlaceholderSubWorkflow` node along the deepest path counts as one level — so it survives process restarts even though `WorkflowRunLoop` tracks the live counter in memory. When the configured limit is `0` or negative the page shows "(limit disabled)"; once the depth reaches the limit the value is highlighted, because the next nested call throws `InvalidOperationException`.

### Applying the fix

With a tail self-call like this:

```csharp
public async IAsyncEnumerable<Wait> Review(ReviewState state)
{
    yield return WaitSignal<ReviewFeedbackEvent>("ReviewFeedback", "Wait for reviewer feedback");

    if (state.Approved) yield break;

    yield return WaitSubWorkflow(Review(new ReviewState()), "Review", "Recurse");
}
```

the fix produces:

```csharp
public async IAsyncEnumerable<Wait> Review(ReviewState state)
{
    while (true)
    {
        yield return WaitSignal<ReviewFeedbackEvent>("ReviewFeedback", "Wait for reviewer feedback");

        if (state.Approved) yield break;

        state = new ReviewState();
        continue;
    }
}
```

The rewrite is deliberately conservative: the loop body is the original body up to the self-call, locals are re-initialized per iteration exactly as a fresh recursive call would, and the self-call's state argument is assigned back to the parameter before `continue`. When the self-call is not in tail position — or the method takes more than the state parameter — no fix is offered, because flattening would change behaviour.

---

## Performance Considerations

- **Unified JSON Storage:** Key-Value caching and document persistence remain O(1) regardless of sub-workflow nesting depth.
- **Stateless Execution:** State transitions are fast because the parent/child workflow share the same container instance, avoiding double instantiation overhead.
