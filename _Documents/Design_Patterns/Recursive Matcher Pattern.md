# Recursive Completion Checker Pattern

## Overview
The checker layer implements a recursive parent-chain propagation pattern where WorkflowExecutionContext is injected via DI (scoped), and wait DTOs are passed explicitly to IsCompleted. WorkflowCompletionChecker is special - it actually executes the sub-workflow to completion rather than just validating an event.

> Note: the evaluator classes described here were later renamed to completion checkers: `WaitCompletionChecker`, `SignalCompletionChecker`, `TimeWaitMatcher`, `CommandCompletionChecker`, `GroupCompletionChecker`, `WorkflowCompletionChecker`. `MatcherFactory` became `CompletionCheckerFactory` and `MatchAsync` became `IsCompleted`. The filename is kept so existing links keep working.

## Architecture

## Key Principles
1. **WorkflowExecutionContext is scoped and injected via DI** - not passed as parameter
2. **Each checker accepts only the wait DTO** - `IsCompleted(WaitInfrastructureDto waitDto)`
3. **Completion Checkers work purely with DTOs** - no Wait object conversion (except WorkflowCompletionChecker)
4. **Completion Checkers are created per-request** - resolved from IServiceProvider via CompletionCheckerFactory
5. **WorkflowCompletionChecker is special** - needs Wait object to access Runner stream
6. **Parents can be GroupWait OR SubWorkflowWait** - both are handled via factory resolution

### Flow
1. A child wait (Signal, Time, or Command) matches successfully
2. The child checker marks its DTO as `WaitStatus.Completed`
3. If the child has a `ParentWaitId`, it calls `CheckParentAsync(...)`
4. The base class helper finds the parent DTO and resolves the appropriate parent checker via `CompletionCheckerFactory`
5. The parent checker is invoked recursively:
   - **GroupCompletionChecker** if parent is a GroupWait (evaluates group condition)
   - **WorkflowCompletionChecker** if parent is a SubWorkflowWait (executes sub-workflow)
6. This process continues until the root wait is evaluated

## Implementation Details

### Base Class: `WaitCompletionChecker`
```csharp
public abstract Task<bool> IsCompleted(WaitInfrastructureDto waitDto);

protected async Task<bool> CheckParentAsync(
    Guid parentWaitId,
    WorkflowExecutionContext context,
    CompletionCheckerFactory checkerFactory)
```

- `IsCompleted` now accepts only the wait DTO - context is injected via DI
- `CheckParentAsync` finds parent DTO from `context.WorkflowState.Waits`
- Resolves parent checker via `checkerFactory.GetCompletion Checker(parentWaitDto)`
- Recursively calls `IsCompleted(parentWaitDto)` on parent
- Shares the same context throughout the recursive chain

### Completion Checker Classes

#### SignalCompletionChecker
- Injected dependencies: `IWorkflowRegistry`, `WorkflowExecutionContext`, `CompletionCheckerFactory`
- **Works purely with DTOs** - no Wait object conversion
- Signature: `IsCompleted(WaitInfrastructureDto waitDto)`
- Validates signal identifier match from DTO
- TODO: Match expression evaluation and AfterMatchAction need DTO support
- After successful signal match:
  - Marks DTO as `Completed`
  - Propagates to parent if `ParentWaitId.HasValue`

#### CommandCompletionChecker
- Injected dependencies: `WorkflowExecutionContext`, `CompletionCheckerFactory`
- **Works purely with DTOs** - no Wait object conversion
- Signature: `IsCompleted(WaitInfrastructureDto waitDto)`
- Handles command result from context
- TODO: OnResultAction and OnFailureAction need DTO support
- After handling result:
  - Marks DTO as `Completed`
  - Propagates to parent if `ParentWaitId.HasValue`

#### TimeWaitCompletion Checker
- Injected dependencies: `WorkflowExecutionContext`, `CompletionCheckerFactory`
- **Works purely with DTOs** - no Wait object conversion
- Signature: `IsCompleted(WaitInfrastructureDto waitDto)`
- Time boundary already validated by orchestrator
- Marks DTO as `Completed`
- Propagates to parent if `ParentWaitId.HasValue`

#### GroupCompletionChecker
- Injected dependencies: `WorkflowExecutionContext`, `CompletionCheckerFactory`
- **Works purely with DTOs** - no Wait object conversion
- **Only called via parent propagation** - never directly by runner
- Signature: `IsCompleted(WaitInfrastructureDto waitDto)`
- Evaluates group condition from DTO (MatchAll, MatchAny)
- TODO: MatchIf custom expression needs DTO support
- If group matches:
  - Marks DTO as `Completed`
  - Prunes remaining children if needed
  - Propagates to parent if `ParentWaitId.HasValue` (supports nested groups)

#### WorkflowCompletionChecker (SPECIAL CASE)
- Injected dependencies: `WorkflowExecutionContext`, `CompletionCheckerFactory`, `StateMachineAdvancer`, `ProcessorFactory`, `CancelTokensHandler`
- **ONLY checker that needs Wait object** - requires `SubWorkflowWait.Runner` stream
- **This checker actually EXECUTES the sub-workflow to completion**
- Signature: `IsCompleted(WaitInfrastructureDto waitDto)`
- Flow:
  1. Gets SubWorkflowWait from context.TriggeringWait (needs Wait for Runner)
  2. Gets or creates child workflow state from `ActiveState.StateMachinesObjects`
  3. Runs the sub-workflow stream using `StateMachineAdvancer`
  4. Processes each yielded wait via `ProcessorFactory`
  5. Handles cancellation via `CancelTokensHandler`
  6. If sub-workflow suspends on passive wait → returns false, stores child state
  7. If sub-workflow completes → marks SubWorkflowWaitDto as `Completed`, removes child state
  8. Propagates to parent if `ParentWaitId.HasValue` (e.g., GroupWait containing sub-workflow)

### CompletionCheckerFactory
- Injected dependency: `IServiceProvider`
- Resolves checkers per-request from DI container
- Completion Checkers are registered as **scoped** because they depend on scoped `WorkflowExecutionContext`

### RefactoredWorkflowRunner
- Gets triggering wait DTO via `WorkflowStateService.FindWaitById`
- Resolves checker via factory: `checkerFactory.GetCompletion Checker(triggeringWaitDto)`
- Invokes checker: `checker.IsCompleted(triggeringWaitDto)` - no context passed

### WorkflowExecutionContext
- Registered as **scoped** in DI
- Injected into checkers automatically
- Contains all execution state (Signal, CommandResult, WorkflowState, WorkflowInstance, etc.)

### DI Registration
```csharp
services.AddScoped<WorkflowExecutionContext>();
services.AddScoped<SignalCompletionChecker>();
services.AddScoped<TimeWaitCompletion Checker>();
services.AddScoped<CommandCompletionChecker>();
services.AddScoped<GroupCompletionChecker>();
services.AddScoped<WorkflowCompletionChecker>();
services.AddSingleton<CompletionCheckerFactory>();
```

## Benefits

1. **Clean Dependency Injection**: Context is scoped and injected, not passed around
2. **Per-Request Completion Checkers**: Fresh instances for each workflow execution
3. **Decoupled Logic**: Each checker only knows about its own matching rules
4. **Automatic Parent Evaluation**: No orchestrator logic needed to check parents
5. **Nested Group Support**: Multi-level groups work automatically through recursion
6. **Sub-workflow Execution**: WorkflowCompletionChecker runs sub-workflows inline
7. **Status Propagation**: `WaitStatus.Completed` is set at each level during upward traversal
8. **Pruning Integration**: GroupCompletionChecker prunes children inline during parent evaluation

## Example Flows

### Scenario 1: Simple GroupWait with Signal Children
```
Signal arrives → RefactoredWorkflowRunner
  ↓
  SignalCompletionChecker.IsCompleted(signalWaitDto) [context injected]
  ↓
  Marks SignalWaitDto as Completed
  ↓
  Finds ParentWaitId (GroupWait)
  ↓
  Resolves GroupCompletionChecker via CompletionCheckerFactory (from DI)
  ↓
  GroupCompletionChecker.IsCompleted(groupWaitDto) [context injected]
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
Signal arrives → SignalCompletionChecker matches signal wait inside sub-workflow
  ↓
  Marks SignalWaitDto as Completed
  ↓
  Finds ParentWaitId (SubWorkflowWait)
  ↓
  Resolves WorkflowCompletionChecker via CompletionCheckerFactory
  ↓
  WorkflowCompletionChecker.IsCompleted(subWorkflowWaitDto)
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
Signal1 arrives → SignalCompletionChecker → GroupCompletionChecker (1 of 2 children done)
  ↓
SubWorkflow1 triggered → WorkflowCompletionChecker executes sub-workflow to completion
  ↓
WorkflowCompletionChecker marks SubWorkflowWaitDto as Completed
  ↓
Propagates to GroupCompletionChecker
  ↓
GroupCompletionChecker checks: all children completed (2 of 2)
  ↓
Marks GroupWaitDto as Completed
```

### Scenario 4: Nested Groups with Sub-workflows
```
Signal in nested sub-workflow → SignalCompletionChecker
  ↓
  ParentWaitId → WorkflowCompletionChecker (executes sub-workflow)
  ↓
  ParentWaitId → GroupCompletionChecker (inner group in parent workflow)
  ↓
  ParentWaitId → WorkflowCompletionChecker (outer sub-workflow)
  ↓
  ParentWaitId → GroupCompletionChecker (root group)
  ↓
  Root reached
```

## Key Differences from Previous Design

1. **Context Injection**: Context is no longer passed as parameter - it's injected via DI
2. **Scoped Completion Checkers**: Completion Checkers are created per-request from DI container
3. **WorkflowCompletionChecker Execution**: Sub-workflow checker actually runs the sub-workflow, not just validates
4. **Simpler API**: `IsCompleted(waitDto)` instead of `IsCompleted(waitDto, context)`
5. **DTO-Only Matching**: Most checkers work purely with DTOs - no Wait object conversion needed
6. **WorkflowCompletionChecker Exception**: Only WorkflowCompletionChecker needs Wait object to access Runner stream

## Notes
- Completion Checkers are **scoped** because they depend on scoped `WorkflowExecutionContext`
- CompletionCheckerFactory resolves checkers from `IServiceProvider` on each call
- **Most checkers work with DTOs only** - no Wait object conversion overhead
- **WorkflowCompletionChecker is the exception** - needs Wait.Runner to execute sub-workflow
- WorkflowCompletionChecker is the only checker that advances state machines - others just validate
- `WorkflowStateService.FindWaitById` is public for runner access
- Parent context is shared throughout the recursive chain


