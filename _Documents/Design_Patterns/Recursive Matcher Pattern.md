# Recursive Matcher Pattern Implementation (Final)

## Overview
The matcher layer implements a recursive parent-chain propagation pattern where WorkflowExecutionContext is injected via DI (scoped), and wait DTOs are passed explicitly to MatchAsync. SubWorkflowWaitMatcher is special - it actually executes the sub-workflow to completion rather than just validating an event.

## Architecture

## Key Principles
1. **WorkflowExecutionContext is scoped and injected via DI** - not passed as parameter
2. **Each matcher accepts only the wait DTO** - `MatchAsync(WaitInfrastructureDto waitDto)`
3. **Matchers work purely with DTOs** - no Wait object conversion (except SubWorkflowWaitMatcher)
4. **Matchers are created per-request** - resolved from IServiceProvider via MatcherFactory
5. **SubWorkflowWaitMatcher is special** - needs Wait object to access Runner stream
6. **Parents can be GroupWait OR SubWorkflowWait** - both are handled via factory resolution

### Flow
1. A child wait (Signal, Time, or Command) matches successfully
2. The child matcher marks its DTO as `WaitStatus.Completed`
3. If the child has a `ParentWaitId`, it calls `MatchParentAsync(...)`
4. The base class helper finds the parent DTO and resolves the appropriate parent matcher via `MatcherFactory`
5. The parent matcher is invoked recursively:
   - **GroupWaitMatcher** if parent is a GroupWait (evaluates group condition)
   - **SubWorkflowWaitMatcher** if parent is a SubWorkflowWait (executes sub-workflow)
6. This process continues until the root wait is evaluated

## Implementation Details

### Base Class: `WorkflowWaitMatcher`
```csharp
public abstract Task<bool> MatchAsync(WaitInfrastructureDto waitDto);

protected async Task<bool> MatchParentAsync(
    Guid parentWaitId,
    WorkflowExecutionContext context,
    MatcherFactory matcherFactory)
```

- `MatchAsync` now accepts only the wait DTO - context is injected via DI
- `MatchParentAsync` finds parent DTO from `context.WorkflowState.Waits`
- Resolves parent matcher via `matcherFactory.GetMatcher(parentWaitDto)`
- Recursively calls `MatchAsync(parentWaitDto)` on parent
- Shares the same context throughout the recursive chain

### Matcher Classes

#### SignalWaitMatcher
- Injected dependencies: `IWorkflowRegistry`, `WorkflowExecutionContext`, `MatcherFactory`
- **Works purely with DTOs** - no Wait object conversion
- Signature: `MatchAsync(WaitInfrastructureDto waitDto)`
- Validates signal identifier match from DTO
- TODO: Match expression evaluation and AfterMatchAction need DTO support
- After successful signal match:
  - Marks DTO as `Completed`
  - Propagates to parent if `ParentWaitId.HasValue`

#### DeferredCommandMatcher
- Injected dependencies: `WorkflowExecutionContext`, `MatcherFactory`
- **Works purely with DTOs** - no Wait object conversion
- Signature: `MatchAsync(WaitInfrastructureDto waitDto)`
- Handles command result from context
- TODO: OnResultAction and OnFailureAction need DTO support
- After handling result:
  - Marks DTO as `Completed`
  - Propagates to parent if `ParentWaitId.HasValue`

#### TimeWaitMatcher
- Injected dependencies: `WorkflowExecutionContext`, `MatcherFactory`
- **Works purely with DTOs** - no Wait object conversion
- Signature: `MatchAsync(WaitInfrastructureDto waitDto)`
- Time boundary already validated by orchestrator
- Marks DTO as `Completed`
- Propagates to parent if `ParentWaitId.HasValue`

#### GroupWaitMatcher
- Injected dependencies: `WorkflowExecutionContext`, `MatcherFactory`
- **Works purely with DTOs** - no Wait object conversion
- **Only called via parent propagation** - never directly by runner
- Signature: `MatchAsync(WaitInfrastructureDto waitDto)`
- Evaluates group condition from DTO (MatchAll, MatchAny)
- TODO: MatchIf custom expression needs DTO support
- If group matches:
  - Marks DTO as `Completed`
  - Prunes remaining children if needed
  - Propagates to parent if `ParentWaitId.HasValue` (supports nested groups)

#### SubWorkflowWaitMatcher (SPECIAL CASE)
- Injected dependencies: `WorkflowExecutionContext`, `MatcherFactory`, `StateMachineAdvancer`, `ProcessorFactory`, `CancelProcessor`
- **ONLY matcher that needs Wait object** - requires `SubWorkflowWait.Runner` stream
- **This matcher actually EXECUTES the sub-workflow to completion**
- Signature: `MatchAsync(WaitInfrastructureDto waitDto)`
- Flow:
  1. Gets SubWorkflowWait from context.TriggeringWait (needs Wait for Runner)
  2. Gets or creates child workflow state from `ActiveState.StateMachinesObjects`
  3. Runs the sub-workflow stream using `StateMachineAdvancer`
  4. Processes each yielded wait via `ProcessorFactory`
  5. Handles cancellation via `CancelProcessor`
  6. If sub-workflow suspends on passive wait → returns false, stores child state
  7. If sub-workflow completes → marks SubWorkflowWaitDto as `Completed`, removes child state
  8. Propagates to parent if `ParentWaitId.HasValue` (e.g., GroupWait containing sub-workflow)

### MatcherFactory
- Injected dependency: `IServiceProvider`
- Resolves matchers per-request from DI container
- Matchers are registered as **scoped** because they depend on scoped `WorkflowExecutionContext`

### RefactoredWorkflowRunner
- Gets triggering wait DTO via `WorkflowStateService.FindWaitById`
- Resolves matcher via factory: `matcherFactory.GetMatcher(triggeringWaitDto)`
- Invokes matcher: `matcher.MatchAsync(triggeringWaitDto)` - no context passed

### WorkflowExecutionContext
- Registered as **scoped** in DI
- Injected into matchers automatically
- Contains all execution state (Signal, CommandResult, WorkflowState, WorkflowInstance, etc.)

### DI Registration
```csharp
services.AddScoped<WorkflowExecutionContext>();
services.AddScoped<SignalWaitMatcher>();
services.AddScoped<TimeWaitMatcher>();
services.AddScoped<DeferredCommandMatcher>();
services.AddScoped<GroupWaitMatcher>();
services.AddScoped<SubWorkflowWaitMatcher>();
services.AddSingleton<MatcherFactory>();
```

## Benefits

1. **Clean Dependency Injection**: Context is scoped and injected, not passed around
2. **Per-Request Matchers**: Fresh instances for each workflow execution
3. **Decoupled Logic**: Each matcher only knows about its own matching rules
4. **Automatic Parent Evaluation**: No orchestrator logic needed to check parents
5. **Nested Group Support**: Multi-level groups work automatically through recursion
6. **Sub-workflow Execution**: SubWorkflowWaitMatcher runs sub-workflows inline
7. **Status Propagation**: `WaitStatus.Completed` is set at each level during upward traversal
8. **Pruning Integration**: GroupWaitMatcher prunes children inline during parent evaluation

## Example Flows

### Scenario 1: Simple GroupWait with Signal Children
```
Signal arrives → RefactoredWorkflowRunner
  ↓
  SignalWaitMatcher.MatchAsync(signalWaitDto) [context injected]
  ↓
  Marks SignalWaitDto as Completed
  ↓
  Finds ParentWaitId (GroupWait)
  ↓
  Resolves GroupWaitMatcher via MatcherFactory (from DI)
  ↓
  GroupWaitMatcher.MatchAsync(groupWaitDto) [context injected]
  ↓
  If MatchAny and 1 child completed → group matches
  ↓
  Marks GroupWaitDto as Completed
  ↓
  Prunes remaining children
  ↓
  Checks GroupWait's ParentWaitId (if nested)
  ↓
  Continues upward until root
```

### Scenario 2: Sub-workflow Triggered by Signal
```
Signal arrives → SignalWaitMatcher matches signal wait inside sub-workflow
  ↓
  Marks SignalWaitDto as Completed
  ↓
  Finds ParentWaitId (SubWorkflowWait)
  ↓
  Resolves SubWorkflowWaitMatcher via MatcherFactory
  ↓
  SubWorkflowWaitMatcher.MatchAsync(subWorkflowWaitDto)
  ↓
  Executes sub-workflow to completion:
    - Advances sub-workflow state machine
    - Processes yielded waits
    - Handles cancellation
    - Continues until completion or passive wait
  ↓
  If completed → Marks SubWorkflowWaitDto as Completed
  ↓
  Propagates to parent if present (e.g., GroupWait)
```

### Scenario 3: Sub-workflow in GroupWait
```
GroupWait: MatchAll(SubWorkflow1, Signal1)
  ↓
Signal1 arrives → SignalWaitMatcher → GroupWaitMatcher (1 of 2 children done)
  ↓
SubWorkflow1 triggered → SubWorkflowWaitMatcher executes sub-workflow to completion
  ↓
SubWorkflowWaitMatcher marks SubWorkflowWaitDto as Completed
  ↓
Propagates to GroupWaitMatcher
  ↓
GroupWaitMatcher checks: all children completed (2 of 2)
  ↓
Marks GroupWaitDto as Completed
```

### Scenario 4: Nested Groups with Sub-workflows
```
Signal in nested sub-workflow → SignalWaitMatcher
  ↓
  ParentWaitId → SubWorkflowWaitMatcher (executes sub-workflow)
  ↓
  ParentWaitId → GroupWaitMatcher (inner group in parent workflow)
  ↓
  ParentWaitId → SubWorkflowWaitMatcher (outer sub-workflow)
  ↓
  ParentWaitId → GroupWaitMatcher (root group)
  ↓
  Root reached
```

## Key Differences from Previous Design

1. **Context Injection**: Context is no longer passed as parameter - it's injected via DI
2. **Scoped Matchers**: Matchers are created per-request from DI container
3. **SubWorkflowWaitMatcher Execution**: Sub-workflow matcher actually runs the sub-workflow, not just validates
4. **Simpler API**: `MatchAsync(waitDto)` instead of `MatchAsync(waitDto, context)`
5. **DTO-Only Matching**: Most matchers work purely with DTOs - no Wait object conversion needed
6. **SubWorkflowWaitMatcher Exception**: Only SubWorkflowWaitMatcher needs Wait object to access Runner stream

## Notes
- Matchers are **scoped** because they depend on scoped `WorkflowExecutionContext`
- MatcherFactory resolves matchers from `IServiceProvider` on each call
- **Most matchers work with DTOs only** - no Wait object conversion overhead
- **SubWorkflowWaitMatcher is the exception** - needs Wait.Runner to execute sub-workflow
- SubWorkflowWaitMatcher is the only matcher that advances state machines - others just validate
- `WorkflowStateService.FindWaitById` is public for runner access
- Parent context is shared throughout the recursive chain


