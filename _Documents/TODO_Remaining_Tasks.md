# TODO: Remaining Implementation Tasks

## Overview
This document tracks remaining tasks to complete the stateless/stateful callback implementation.

---

## ✅ Completed

1. **CommandBuilder** - Added missing `implicit operator Wait` conversions
2. **WorkflowTemplateCache** - Added all invoker methods with 3-parameter signatures
3. **WorkflowRunner** - Updated to pass `ExplicitState` to all callbacks
4. **StateMachineObject** - Already has `WaitStatesObjects` dictionary
5. **Mapper** - Save and restore `ExplicitState` from/to `StateMachineObject`
6. **Signal Processing** - Pass state to `MatchIf` and `AfterMatch`
7. **Command Scaffolding** - Added `ExecuteCommandAsync` method structure

---

## ⏳ TODO

### 1. Complete Command Execution Implementation

**File:** `Workflows.Runner\WorkflowRunner.cs`  
**Method:** `ExecuteCommandAsync`

**Current State:** Placeholder implementation

**What's Needed:**
```csharp
// Replace this placeholder:
object result = null; // Replace with actual handler execution

// With actual handler invocation:
var handler = _commandHandlerFactory.GetHandler(commandWait.HandlerKey);
var result = await handler.ExecuteAsync(commandData);
```

**Considerations:**
- Handle `CommandExecutionMode.Direct` vs `CommandExecutionMode.Indirect`
- Implement retry logic based on `MaxRetryAttempts` and `RetryBackoff`
- Store command result for compensation if needed
- Handle serialization of command data and result

---

### 2. Implement Compensation Logic (Saga Pattern)

**File:** `Workflows.Runner\WorkflowRunner.cs` (or new file)

**What's Needed:**
- Track compensatable commands in workflow state
- Implement compensation invocation when `Compensate(token)` is yielded
- Call `CompensationAction` for each registered compensation
- Pass `ExplicitState` to compensation callbacks

**Example Flow:**
```csharp
// When command succeeds
if (commandWait.CompensationAction != null)
{
    // Store for later compensation
    storeCompensation(commandWait.CompensationTokens, result, explicitState);
}

// When Compensate(token) is yielded
if (wait is CompensationWait compensationWait)
{
    var compensations = getCompensations(compensationWait.Token);
    foreach (var comp in compensations.Reverse())
    {
        await InvokeCompensationAsync(comp.Action, comp.Result, comp.State);
    }
}
```

---

### 3. Implement Cancel Action Invocation

**File:** `Workflows.Runner\WorkflowRunner.cs`

**What's Needed:**
- Detect when a wait is canceled (via `CancelToken` matching)
- Invoke `Wait.CancelAction` before removing the wait
- Pass `ExplicitState` to cancel callbacks

**Example Flow:**
```csharp
// When workflow calls CancelToken(token)
var waitsToCancel = FindWaitsByCancelToken(token);
foreach (var wait in waitsToCancel)
{
    if (wait.CancelAction != null)
    {
        await InvokeCancelActionAsync(wait.CancelAction, workflowInstance, wait.ExplicitState);
    }
    RemoveWait(wait);
}
```

**Cache Method:** Already implemented
```csharp
public Func<object, object, object, ValueTask> GetOrAddCancelActionInvoker(Type actionType)
```

---

### 4. Implement Group Filter Evaluation

**File:** `Workflows.Runner\WorkflowRunner.cs` or `ExpressionCompiler.cs`

**What's Needed:**
- Evaluate `GroupWait.GroupMatchFilter` when group waits are matched
- Support both stateless `Func<bool>` and stateful `Func<TState, bool>`
- Pass `WorkflowContainer` instance and `ExplicitState`

**Example Flow:**
```csharp
if (wait is GroupWait groupWait && groupWait.GroupMatchFilter != null)
{
    var invoker = _templateCache.GetOrAddGroupFilterInvoker(groupWait.GroupMatchFilter.GetType());
    var matches = invoker(groupWait.GroupMatchFilter, workflowInstance, groupWait.ExplicitState);

    if (!matches)
    {
        // Skip this group
        continue;
    }
}
```

**Cache Method:** Already implemented
```csharp
public Func<object, object, object, bool> GetOrAddGroupFilterInvoker(Type filterType)
```

---

### 5. Update ExpressionCompiler Implementations

**File:** `Workflows.Runner\ExpressionTransformers\ExpressionCompiler.cs`

**Current State:** All methods throw `NotImplementedException()`

**Methods to Implement:**

#### a. `CompiledMatchExpression` ✅ (Already exists in WorkflowRunner.CompileMatch)
- May need to move to ExpressionCompiler for consistency

#### b. `AfterMatchAction`
```csharp
internal Action<object, object, object> AfterMatchAction<SignalData>(Action<SignalData> afterMatchAction)
{
    // Wrap stateless action to accept 3 parameters (action, signal, state)
    return (action, signal, state) => 
    {
        var typedAction = (Action<SignalData>)action;
        var typedSignal = (SignalData)signal;
        typedAction(typedSignal);
    };
}
```

#### c. `CancelAction`
```csharp
internal Func<object, object, ValueTask> CancelAction(Func<ValueTask> cancelAction)
{
    // Wrap stateless cancel action to accept 2 parameters (action, state)
    return (action, state) => 
    {
        var typedAction = (Func<ValueTask>)action;
        return typedAction();
    };
}
```

#### d. `GroupMatchFilter`
```csharp
public Func<object, object, bool> GroupMatchFilter(Func<bool> groupMatchFilter)
{
    // Wrap stateless filter to accept 2 parameters (filter, state)
    return (filter, state) => 
    {
        var typedFilter = (Func<bool>)filter;
        return typedFilter();
    };
}
```

---

### 6. Handle TimeWait Expiration

**File:** `Workflows.Runner\WorkflowRunner.cs` or Orchestrator

**What's Needed:**
- Detect when `TimeWait` duration expires
- Trigger workflow advancement
- Invoke `OnCanceled` callback if wait is canceled before expiry

**Note:** This might be handled in Orchestrator layer, not Runner

---

### 7. Handle SubWorkflow Completion

**File:** `Workflows.Runner\WorkflowRunner.cs`

**What's Needed:**
- Detect when `SubWorkflowWait` completes
- Resume parent workflow
- Propagate any result data if needed

**Note:** This might be handled in Orchestrator layer, not Runner

---

### 8. Build Roslyn Analyzer

**Project:** `Workflows.Analyzers`

**Rules to Enforce:**

#### WF001: No Closure Captures
```csharp
// ❌ Error
var minOrderId = 100;
yield return WaitSignal<OrderEvent>("OrderReceived")
    .MatchIf(order => order.OrderId > minOrderId); // Captures closure

// ✅ Fix
yield return WaitSignal<OrderEvent>("OrderReceived")
    .WithState(100)
    .MatchIf((order, minId) => order.OrderId > minId);
```

#### WF002: No await foreach for Sub-Workflows
```csharp
// ❌ Error
await foreach(var wait in ChildWorkflow()) 
{
    yield return wait;
}

// ✅ Fix
yield return WaitSubWorkflow(ChildWorkflow());
```

#### WF003: No yield inside using block
```csharp
// ❌ Error
using (var context = new MyDbContext()) 
{
    yield return Wait("User Input");
}

// ✅ Fix: Move resource outside or use between yields
```

#### WF004: No AsyncLocal or HttpContext
```csharp
// ❌ Error
AsyncLocal<string> tenantId = new();
yield return Wait("..."); // Will lose tenantId

// ✅ Fix: Store in WorkflowContainer property
public string TenantId { get; set; }
```

#### WF005: No non-serializable state
```csharp
// ❌ Error
FileStream stream = ...;
yield return Wait("..."); // stream can't be serialized

// ✅ Fix: Use between yields only, don't hold across yields
```

---

### 9. Add Integration Tests

**Test Scenarios:**

1. **State Persistence Test**
   - Create workflow with `.WithState(complexObject)`
   - Suspend workflow
   - Verify `StateMachineObject.WaitStatesObjects` contains state
   - Resume workflow
   - Verify state restored correctly

2. **State Deduplication Test**
   - Create multiple waits sharing same state object
   - Verify state stored only once in `WaitStatesObjects`

3. **Command Execution Test**
   - Execute command with `OnResult` callback
   - Verify callback invoked with correct state
   - Test retry logic
   - Test failure path with `OnFailure`

4. **Compensation Test**
   - Register compensations on multiple commands
   - Trigger compensation
   - Verify compensations run in reverse order
   - Verify state passed to each compensation

5. **Cancel Action Test**
   - Create wait with `OnCanceled` callback
   - Cancel the wait
   - Verify callback invoked with state

6. **Group Filter Test**
   - Create `GroupWait` with stateful `MatchIf`
   - Verify filter evaluated with state
   - Test both pass and fail scenarios

---

### 10. Update Documentation

**Files to Update:**

1. **README.md** - Add state management overview
2. **API Reference** - Document `.WithState<TState>()` pattern
3. **Migration Guide** - Help users migrate from closure-based code
4. **Examples** - Add more stateful callback examples
5. **Architecture Guide** - Update with state persistence flow

---

## Priority Order

### High Priority (Core Functionality)
1. ✅ Complete Command Execution Implementation
2. ✅ Implement Compensation Logic
3. ✅ Implement Cancel Action Invocation
4. ✅ Implement Group Filter Evaluation

### Medium Priority (Developer Experience)
5. ✅ Build Roslyn Analyzer
6. ✅ Add Integration Tests
7. ✅ Update Documentation

### Low Priority (Nice to Have)
8. ✅ Update ExpressionCompiler (may not be needed if cache methods work)
9. ✅ Handle TimeWait Expiration (might be Orchestrator's job)
10. ✅ Handle SubWorkflow Completion (might be Orchestrator's job)

---

## Questions for Clarification

1. **Command Handler Integration**
   - Is `ICommandHandlerFactory` already implemented?
   - Should commands be executed synchronously or via message queue?
   - Where should command results be stored for compensation?

2. **Compensation Storage**
   - Should compensations be stored in `StateMachineObject`?
   - Or in a separate database table?
   - How long should compensation history be kept?

3. **Time-Based Waits**
   - Is `TimeWait` triggered by Orchestrator polling?
   - Or by external timer service?
   - Should Runner handle expiration or just Orchestrator?

4. **SubWorkflows**
   - Are sub-workflows separate workflow instances?
   - How is parent-child relationship tracked?
   - Where is sub-workflow state stored?

---

**Last Updated:** December 2024  
**Status:** Core implementation complete, integration tasks remaining
