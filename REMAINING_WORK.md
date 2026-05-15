# Remaining Work for Complete Runner Implementation

## ✅ What's Done (This Commit)

### Runner-Side Implementation (100% Complete)
1. ✅ Compensation (Saga Pattern)
   - LIFO execution
   - Token-based filtering
   - Result preservation
   - History tracking

2. ✅ Cancellation
   - Token checking
   - OnCanceled callbacks
   - State synchronization
   - Automatic skip logic

3. ✅ Enhanced Command Execution
   - Direct/Dispatched modes
   - History tracking
   - OnResult/OnFailure callbacks

4. ✅ State Management
   - ExplicitState preservation
   - Command history storage
   - CancelledTokens persistence

---

## ⚠️ What's Needed Next

### 1. Group Wait Evaluation (Orchestrator + Runner)
**Priority:** HIGH
**Complexity:** MEDIUM
**Estimated Effort:** 4-6 hours

#### Runner-Side Changes Needed
```csharp
// In RunWorkflowAsync, after getting nextWait:
if (nextWait is GroupWait groupWait)
{
    // Check if group is completed based on semantics
    var completedCount = groupWait.ChildWaits.Count(w => w.Status == WaitStatus.Completed);

    if (groupWait.MatchSemantics == GroupMatchSemantics.MatchAll)
    {
        if (completedCount == groupWait.ChildWaits.Count)
        {
            // All children complete - group is complete
            // Continue to next wait
        }
        else
        {
            // Still waiting for children
            // Return group wait to orchestrator
        }
    }
    else if (groupWait.MatchSemantics == GroupMatchSemantics.MatchAny)
    {
        if (completedCount >= 1)
        {
            // First child completed - cancel others (downward pruning)
            foreach (var child in groupWait.ChildWaits.Where(w => w.Status == WaitStatus.Waiting))
            {
                // Mark as cancelled in state
                // Invoke OnCanceled if present
            }
            // Continue to next wait
        }
        else
        {
            // Still waiting for first child
            // Return group wait to orchestrator
        }
    }
    else if (groupWait.MatchSemantics == GroupMatchSemantics.Custom)
    {
        // Compile and evaluate custom expression
        var groupFilter = GetOrBuildCompiledGroupFilter(groupWait);
        if (groupFilter(groupWait.ChildWaits, groupWait.ExplicitState))
        {
            // Custom condition met - continue
        }
    }
}
```

#### Orchestrator-Side Changes Needed
- When signal arrives, check if it matches any wait in a group
- Update group's child wait status
- Check group completion semantics
- If group complete, wake runner with group ID as triggering wait
- If MatchAny, prune database entries for sibling waits

**Files to Modify:**
- `Workflows.Runner/WorkflowRunner.cs` - Add group evaluation in RunWorkflowAsync
- `Workflows.Orchestrator/SignalHandler.cs` - Add group checking
- `Workflows.Orchestrator/GroupEvaluator.cs` - NEW class for group logic

---

### 2. Sub-Workflow Context Switching (Runner)
**Priority:** HIGH  
**Complexity:** HIGH  
**Estimated Effort:** 6-8 hours

#### Changes Needed
```csharp
// In RunWorkflowAsync, after getting nextWait:
if (nextWait is SubWorkflowWait subWorkflowWait)
{
    // Get child workflow stream
    var childStream = subWorkflowWait.ChildWorkflowStream;

    // Create child state context
    var childState = new StateMachineObject
    {
        StateIndex = -1,
        Instance = subWorkflowWait.WorkflowContainer,
        StateMachinesObjects = new Dictionary<Guid, object>(),
        WaitStatesObjects = new Dictionary<Guid, object>()
    };

    // If resuming, restore child state
    if (state.StateObject.StateMachinesObjects.TryGetValue(subWorkflowWait.Id, out var savedChildState))
    {
        childState = savedChildState as StateMachineObject;
    }

    // Recursively execute child workflow
    var childAdvancerResult = await _stateMachineAdvancer.RunAsync(childStream, childState);

    if (childAdvancerResult?.Wait != null)
    {
        // Child suspended - save child state and return sub-workflow wait
        state.StateObject.StateMachinesObjects[subWorkflowWait.Id] = childAdvancerResult.State;
        return WaitResult(subWorkflowWait); // Parent waits for child
    }
    else
    {
        // Child completed - continue parent workflow
        // Get next wait from parent
        advancerResult = await _stateMachineAdvancer.RunAsync(workflowStream, state.StateObject);
        nextWait = advancerResult?.Wait;
    }
}
```

**Files to Modify:**
- `Workflows.Runner/WorkflowRunner.cs` - Add sub-workflow context switching
- `Workflows.Runner/StateMachineAdvancer.cs` - May need recursion support
- `Workflows.Definition/SubWorkflowWait.cs` - Ensure ChildWorkflowStream is accessible

---

### 3. Timer-Based Waits (Orchestrator)
**Priority:** MEDIUM  
**Complexity:** LOW  
**Estimated Effort:** 2-3 hours

#### Changes Needed
Runner treats TimeWait as passive (already works correctly). Just needs orchestrator to:

```csharp
// In Orchestrator, when TimeWait is received:
if (wait is TimeWaitDto timeWait)
{
    if (timeWait.WaitType == WaitType.Delay)
    {
        // Schedule timer
        var fireTime = DateTime.UtcNow + timeWait.DelayDuration;
        _timerService.ScheduleCallback(fireTime, timeWait.Id, workflowInstanceId);
    }
    else if (timeWait.WaitType == WaitType.Until)
    {
        // Schedule at specific time
        _timerService.ScheduleCallback(timeWait.UntilDateTime, timeWait.Id, workflowInstanceId);
    }
}

// When timer fires:
public async Task OnTimerExpired(Guid waitId, Guid workflowInstanceId)
{
    // Load workflow state
    // Create execution request with timeWait as triggering wait
    // Send to runner
}
```

**Files to Modify:**
- `Workflows.Orchestrator/WaitProcessor.cs` - Add TimeWait handling
- `Workflows.Orchestrator/Services/TimerService.cs` - NEW service for scheduling
- `Workflows.Orchestrator/Callbacks/TimerCallback.cs` - NEW handler for timer expiry

---

### 4. Database Pruning for Cancelled Waits (Orchestrator)
**Priority:** LOW  
**Complexity:** LOW  
**Estimated Effort:** 1-2 hours

#### Changes Needed
```csharp
// In Orchestrator, after runner returns WorkflowRunResult:
if (result.WorkflowState.CancelledTokens.Any())
{
    foreach (var cancelledToken in result.WorkflowState.CancelledTokens)
    {
        // Find all waits with this token
        var waitsToCancel = await _dbContext.Waits
            .Where(w => w.WorkflowInstanceId == workflowInstanceId)
            .Where(w => w.CancelTokens.Contains(cancelledToken))
            .ToListAsync();

        // Delete from database
        _dbContext.Waits.RemoveRange(waitsToCancel);
        await _dbContext.SaveChangesAsync();
    }
}
```

**Files to Modify:**
- `Workflows.Orchestrator/WorkflowManager.cs` - Add pruning after runner execution
- `Workflows.Orchestrator.Data.EF/DbContext.cs` - Ensure CancelTokens is queryable

---

## Testing Plan for Remaining Features

### Integration Tests Needed

1. **Group Wait Tests**
   - MatchAll: Two signals arrive → both children complete → group completes
   - MatchAny: First signal arrives → group completes immediately → second signal ignored
   - Custom: Complex condition → evaluated correctly → group completes when condition met
   - Nested: Group of groups → inner groups complete → outer group evaluates

2. **Sub-Workflow Tests**
   - Simple: Parent yields sub-workflow → sub executes fully → parent resumes
   - Suspending: Sub-workflow suspends on signal → parent suspended too → signal arrives → sub resumes → parent resumes
   - State: Sub-workflow has state → state preserved across suspension → parent has separate state
   - Nested: Sub-workflow contains another sub-workflow → 3-level nesting works

3. **Timer Tests**
   - Delay: WaitDelay(30s) → 30 seconds pass → workflow resumes
   - Until: WaitUntil(specificDateTime) → that time arrives → workflow resumes
   - Cancellation: Timer scheduled → workflow cancelled → timer cancelled too

4. **End-to-End Tests**
   - Saga: Multiple commands → compensation triggered → all compensated in LIFO
   - Cancellation: Multiple waits with tokens → token cancelled → all waits skipped
   - Groups + Cancellation: Group contains signals with cancel token → token cancelled → group and children cancelled
   - Sub-workflows + Compensation: Parent registers compensation → sub-workflow executes → compensation triggered → parent compensations run

---

## Implementation Order Recommendation

### Phase 1: Core Features (Next Sprint)
1. ✅ Compensation ← DONE
2. ✅ Cancellation ← DONE
3. ✅ Command Execution ← DONE
4. **Group Wait Evaluation** ← NEXT
5. **Timer-Based Waits** ← AFTER GROUPS

### Phase 2: Advanced Features (Sprint +1)
6. **Sub-Workflow Context Switching**
7. **Database Pruning**
8. **Nested Groups** (groups of groups)
9. **Integration Testing**

### Phase 3: Polish (Sprint +2)
10. Performance optimization
11. Error handling improvements
12. Telemetry and logging
13. Documentation updates

---

## Effort Summary

| Feature | Priority | Complexity | Hours | Status |
|---------|----------|------------|-------|--------|
| Compensation | HIGH | HIGH | 8 | ✅ DONE |
| Cancellation | HIGH | MEDIUM | 6 | ✅ DONE |
| Command Execution | HIGH | MEDIUM | 4 | ✅ DONE |
| Group Evaluation | HIGH | MEDIUM | 6 | ⏳ TODO |
| Timer Waits | MEDIUM | LOW | 3 | ⏳ TODO |
| Sub-Workflows | HIGH | HIGH | 8 | ⏳ TODO |
| Database Pruning | LOW | LOW | 2 | ⏳ TODO |
| **Total Completed** | | | **18** | **3/7** |
| **Total Remaining** | | | **19** | **4/7** |

---

## Architecture Decisions Needed

### 1. Group Evaluation - Where?
**Option A:** Runner evaluates groups (current design)
- ✅ Keeps business logic in runner
- ✅ Consistent with runner architecture
- ❌ Requires wait tree in memory

**Option B:** Orchestrator evaluates groups
- ✅ Can query database for wait status
- ❌ Splits business logic across layers
- ❌ Violates "runner does logic" principle

**Recommendation:** Option A (runner evaluates)

### 2. Sub-Workflow State - Where?
**Option A:** Store in parent's StateMachinesObjects[subWorkflowWaitId]
- ✅ Simple, nested structure
- ✅ Serializes naturally
- ❌ Can grow large for deep nesting

**Option B:** Separate state entry in orchestrator DB
- ✅ Flat structure
- ❌ More complex state management
- ❌ Harder to serialize/deserialize

**Recommendation:** Option A (nested in parent state)

### 3. Timer Service - Implementation?
**Option A:** Hangfire for scheduling
- ✅ Robust, proven
- ✅ Handles retries, failures
- ❌ External dependency

**Option B:** Custom in-memory scheduler
- ✅ No dependencies
- ❌ Must implement reliability
- ❌ Restart = lost timers

**Option C:** Azure Durable Functions timers
- ✅ Cloud-native
- ✅ Reliable
- ❌ Azure-specific

**Recommendation:** Option A (Hangfire) for reliability

---

## Success Criteria

### Feature Complete When:
1. ✅ All runner features implemented
2. ⏳ All orchestrator features implemented
3. ⏳ Integration tests pass (21 existing + 20 new = 41 total)
4. ⏳ Documentation updated
5. ⏳ Performance benchmarks meet targets (<100ms per wait)
6. ⏳ Error handling covers edge cases
7. ⏳ Saga pattern works end-to-end
8. ⏳ Cancellation works across all wait types
9. ⏳ Groups work with nested groups
10. ⏳ Sub-workflows work with compensation

---

## Contact for Questions

- **Compensation/Cancellation:** See IMPLEMENTATION_COMPLETE.md
- **Group Logic:** See `_Documents/Runner Evaluation Logic.md`
- **Sub-Workflows:** See `_Documents/State Management Implementation.md`
- **Architecture:** See `_Documents/Architectural Reference Guide.md`

---

**Current Status:** 43% Complete (3 of 7 core features)
**Next Milestone:** Group Wait Evaluation
**Estimated Completion:** 3 sprints
