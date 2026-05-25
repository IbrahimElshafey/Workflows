# Sub-Workflow Execution Implementation

## Overview

Sub-workflows (also known as "resumable functions" or "child workflows") allow workflows to be composed hierarchically. A parent workflow can invoke child workflows that have their own wait points, state management, and execution flow.

## Implementation Status: ✅ COMPLETE

The runner now fully supports sub-workflow context switching and state management.

## Architecture

### Key Components

1. **SubWorkflowWait** (`Workflows.Definition/SubWorkflowWait.cs`)
   - Represents a wait point that triggers child workflow execution
   - Contains `Runner` property: the child's `IAsyncEnumerable<Wait>` enumerator
   - Contains `FirstWait` property: the first wait yielded by the child

2. **State Management** (`StateMachineObject.StateMachinesObjects`)
   - Child workflow states are stored keyed by the parent's `SubWorkflowWait.Id`
   - Each child has its own `StateMachineObject` with isolated state
   - Parent and child share the same workflow instance for property access

3. **Runner Logic** (`WorkflowRunner.ExecuteSubWorkflowAsync`)
   - When parent yields `SubWorkflowWait`, runner immediately executes child
   - Child's first wait is returned and tracked with `ParentWaitId`
   - When child wait completes, runner resumes child (not parent)
   - When child completes (no more waits), runner resumes parent

## Execution Flow

### Initial Sub-Workflow Start

```
1. Parent workflow yields: WaitSubWorkflow(ChildEnumerator(), "ChildName")
2. Runner detects SubWorkflowWait
3. Runner calls ExecuteSubWorkflowAsync:
   - Creates new StateMachineObject for child (StateIndex = -1)
   - Advances child enumerator
   - Saves child state in parent.StateMachinesObjects[SubWorkflowWait.Id]
4. Runner returns child's first wait with ParentWaitId set
5. DTO hierarchy: SubWorkflowWaitDto.ChildWaits = [child's first wait]
```

### Sub-Workflow Resumption

```
1. Signal/Command triggers child wait
2. Runner detects triggeringWait.ParentWaitId is not null
3. Runner finds parent SubWorkflowWaitDto
4. Runner retrieves child state from parent.StateMachinesObjects[SubWorkflowWait.Id]
5. Runner advances child enumerator (not parent)
6. If child yields another wait: save state, return wait
7. If child completes: remove child state, resume parent
```

### Sub-Workflow Completion

```
1. Child enumerator returns null (no more waits)
2. Runner removes child state: parent.StateMachinesObjects.Remove(SubWorkflowWait.Id)
3. Runner resumes parent workflow from where it yielded SubWorkflowWait
4. Parent continues with next statement after WaitSubWorkflow(...)
```

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
    // Sub-workflow completes here, parent resumes
}
```

## Execution Log Output

When the runner executes the above workflow:

```
1. Parent: Start
2. Parent: Order ORD-001
3. SubWorkflow: Start
4. SubWorkflow: Inventory reserved - RES-123
5. SubWorkflow: Payment confirmed - TXN-456
6. SubWorkflow: End
7. Parent: Sub-workflow completed
8. [ShipmentSubWorkflow execution...]
9. Parent: End
```

## State Persistence

### Parent State

```json
{
  "StateIndex": 5,
  "Instance": { /* ParentWorkflow instance */ },
  "StateMachinesObjects": {
    "3fa85f64-5717-4562-b3fc-2c963f66afa6": {  // SubWorkflowWait.Id for "ProcessOrder"
      "StateIndex": 2,
      "Instance": { /* Same workflow instance */ },
      "StateMachinesObjects": {},
      "WaitStatesObjects": {}
    }
  },
  "WaitStatesObjects": {
    "7c9e6679-7425-40de-944b-e07fc1f90ae7": "ShipmentState"
  }
}
```

## Recursive Sub-Workflows

Sub-workflows can themselves contain sub-workflows:

```csharp
private async IAsyncEnumerable<Wait> Level1SubWorkflow()
{
    yield return WaitSubWorkflow(Level2SubWorkflow(), "Level2", "Nested");
    // Level2SubWorkflow can also have sub-workflows (Level3, etc.)
}
```

The runner handles this recursively - each nesting level gets its own state entry.

## Testing Sub-Workflows

### ❌ DSL-Only Tests (Wrong Expectation)

```csharp
// This will NOT execute child workflows!
var workflow = new ParentWorkflow();
var enumerator = workflow.ExecuteWorkflowAsync().GetAsyncEnumerator();

while (await enumerator.MoveNextAsync())
{
    var wait = enumerator.Current;
    // Child enumerator is NOT advanced here
}

// ❌ This will FAIL - child logs not added
workflow.ExecutionLog.Should().Contain("SubWorkflow: Start");
```

**Why it fails:** Directly enumerating the parent only advances the parent's enumerator. The child's `Runner` enumerator is created but never executed.

### ✅ Runner Tests (Correct Approach)

```csharp
// Arrange
var builder = new WorkflowTestBuilder();
builder.RegisterWorkflow<ParentWorkflow>("Parent");
builder.RegisterSignal<OrderSignal>("OrderReceived");

var runner = builder.Build();

var waitId = Guid.NewGuid();
var signalWait = builder.CreateSignalWaitDto("OrderReceived", "Initial", waitId);

var request = builder.CreateExecutionRequest<ParentWorkflow>(
    waitId,
    "Parent",
    waits: new List<WaitInfrastructureDto> { signalWait });

request.Signal = builder.CreateSignal("OrderReceived", new OrderSignal { OrderId = "ORD-001" });

// Act
var result = await runner.RunWorkflowAsync(request);

// Assert - Child execution logs ARE present
workflow.ExecutionLog.Should().Contain("SubWorkflow: Start");
workflow.ExecutionLog.Should().Contain("SubWorkflow: End");
```

**Why it works:** The runner detects `SubWorkflowWait` and calls `ExecuteSubWorkflowAsync`, which advances the child's enumerator.

## Implementation Details

### ExecuteSubWorkflowAsync Method

```csharp
private async Task<Wait> ExecuteSubWorkflowAsync(
    SubWorkflowWait subWorkflowWait,
    StateMachineObject parentState,
    WorkflowContainer parentWorkflowInstance)
{
    // Check if we have a suspended child state (resuming)
    var subWorkflowStateKey = subWorkflowWait.Id;
    StateMachineObject childState = null;

    if (parentState.StateMachinesObjects?.TryGetValue(subWorkflowStateKey, out var storedChildState) == true)
    {
        childState = storedChildState as StateMachineObject;
    }

    // If no child state, this is the first execution
    childState ??= new StateMachineObject
    {
        StateIndex = -1,
        Instance = parentWorkflowInstance,  // Share workflow instance
        StateMachinesObjects = new Dictionary<Guid, object>(),
        WaitStatesObjects = new Dictionary<Guid, object>()
    };

    // Advance child workflow
    var childAdvancerResult = await _stateMachineAdvancer.RunAsync(subWorkflowWait.Runner, childState);

    if (childAdvancerResult?.Wait != null)
    {
        // Child yielded a wait - save child state and return the wait
        parentState.StateMachinesObjects[subWorkflowStateKey] = childAdvancerResult.State;
        return childAdvancerResult.Wait;
    }
    else
    {
        // Child completed - remove child state
        parentState.StateMachinesObjects?.Remove(subWorkflowStateKey);
        return null;  // Signal parent to continue
    }
}
```

### Parent Resumption After Child Completion

When `ExecuteSubWorkflowAsync` returns `null`, the runner knows the child completed and advances the parent:

```csharp
if (advancerResult?.Wait == null && parentSubWorkflow != null)
{
    // Sub-workflow completed - resume parent
    var parentWorkflowInvoker = _templateCache.GetOrAddWorkflowInvoker(...);
    var parentWorkflowStream = (IAsyncEnumerable<Wait>)parentWorkflowInvoker(workflowInstance);

    var parentAdvancerResult = await _stateMachineAdvancer.RunAsync(parentWorkflowStream, state.StateObject);
    // ... handle parent's next wait or completion
}
```

## Limitations

1. **Orchestrator Coordination:** Multi-instance sub-workflows (parallel child execution) require orchestrator logic
2. **Cross-Workflow State:** Child workflows share the parent's workflow instance properties but have isolated state machines
3. **Error Handling:** Sub-workflow exceptions currently propagate to parent; compensation logic spans both parent and child

## Performance Considerations

- **State Size:** Each nesting level adds a `StateMachineObject` entry to `StateMachinesObjects`
- **Recursion Depth:** Deep nesting (10+ levels) may impact serialization performance
- **Memory:** Active child enumerators are held in memory during execution

## Future Enhancements

1. **Sub-Workflow Timeouts:** Add timeout support per sub-workflow
2. **Parallel Sub-Workflows:** Allow parent to spawn multiple children concurrently (orchestrator feature)
3. **Sub-Workflow Compensation:** Dedicated compensation scopes per sub-workflow
4. **Sub-Workflow Metrics:** Track child execution duration, retry counts, etc.

---

## Summary

✅ **Sub-workflow execution is fully implemented in the runner.**

- Parent workflows can yield `WaitSubWorkflow(...)` to invoke child workflows
- Child workflows execute with their own state machines and wait points
- When child completes, parent automatically resumes
- State is preserved across parent-child boundaries
- Recursive sub-workflows are supported
- **Must use the runner** (not DSL-only enumeration) to see full execution behavior

**Testing Guidance:** Always use `WorkflowRunner.RunWorkflowAsync(...)` to test sub-workflows. Direct DSL enumeration will not execute child enumerators.
