# Workflow Runner Refactoring - Phase 1 Complete

## Overview

This document describes the initial phase of refactoring the WorkflowRunner from a monolithic "God Object" into a clean pipeline architecture with separate evaluators and handlers.

## Architecture Components Created

### 1. Core Infrastructure

#### **WorkflowExecutionContext** (`Pipeline/WorkflowExecutionContext.cs`)
- Central context object passed through the entire execution pipeline
- Contains all state needed for evaluation and handling
- Properties include:
  - `IncomingRequest`: The original execution request
  - `WorkflowState`: Current workflow state
  - `WorkflowInstance`: The workflow container instance
  - `TriggeringWait`: The wait that triggered this execution
  - `ContinueExecutionLoop`: Controls whether to keep advancing
  - `NewWaits`: Accumulates waits to persist
  - `ConsumedWaitsIds`: Tracks completed waits
  - `IsWorkflowCompleted`: Indicates workflow completion

### 2. Pipeline Interfaces

#### **IWorkflowWaitEvaluator** (`Pipeline/IWorkflowWaitEvaluator.cs`)
- Evaluates incoming events against wait constraints
- Returns `bool`: `true` to proceed, `false` to abort
- Implemented by type-specific evaluators

#### **IWorkflowWaitHandler** (`Pipeline/IWorkflowWaitHandler.cs`)
- Handles yielded waits after state machine advancement
- Returns `bool`: `true` to continue loop (active), `false` to suspend (passive)
- Implemented by type-specific handlers

#### **IEvaluatorFactory** (`Pipeline/IEvaluatorFactory.cs`)
- Resolves the appropriate evaluator based on triggering wait type
- Factory pattern for evaluator dispatch

#### **IHandlerFactory** (`Pipeline/IHandlerFactory.cs`)
- Resolves the appropriate handler based on yielded wait type
- Factory pattern for handler dispatch

#### **IWorkflowStateService** (`Pipeline/WorkflowStateService.cs`)
- Creates execution contexts from incoming requests
- Maps contexts back to result DTOs for persistence
- Handles workflow instance creation and state restoration

#### **ICancelHandler** (`Pipeline/CancelHandler.cs`)
- Processes cancellation logic
- Executes OnCancel/OnFailure callbacks
- Prunes cancelled sub-trees

### 3. Evaluators (Incoming Event Pipeline)

All evaluators are stateless and implement `IWorkflowWaitEvaluator`.

#### **SignalWaitEvaluator** (`Pipeline/Evaluators/SignalWaitEvaluator.cs`)
- Validates signal identifier match
- Executes cached MatchIf template filters
- Invokes AfterMatchAction on success
- Returns `false` for partial matches or failures

#### **TimeWaitEvaluator** (`Pipeline/Evaluators/TimeWaitEvaluator.cs`)
- Validates timer boundaries are satisfied
- Simple pass-through (orchestrator handles scheduling)

#### **DeferredCommandEvaluator** (`Pipeline/Evaluators/DeferredCommandEvaluator.cs`)
- Processes command callback results
- Maps results to CommandWait
- Invokes OnResultAction lambda

#### **GroupWaitEvaluator** (`Pipeline/Evaluators/GroupWaitEvaluator.cs`)
- Evaluates compound boolean conditions (MatchAll/MatchAny)
- Handles branch pruning on fulfillment
- *(Placeholder for future implementation)*

### 4. Handlers (Outgoing Wait Pipeline)

All handlers are stateless and implement `IWorkflowWaitHandler`.

#### **SignalWaitHandler** (`Pipeline/Handlers/SignalWaitHandler.cs`)
- Extracts and transforms MatchExpression structures
- Updates exact-match template indexes
- Returns `false` (passive wait, suspend execution)

#### **TimeWaitHandler** (`Pipeline/Handlers/TimeWaitHandler.cs`)
- Calculates absolute datetime offsets
- Registers schedules
- Returns `false` (passive wait)

#### **ImmediateCommandHandler** (`Pipeline/Handlers/ImmediateCommandHandler.cs`)
- Resolves handler from ICommandHandlerFactory
- Executes command instantly in RAM
- Returns `true` (active wait, continue loop)

#### **DeferredCommandHandler** (`Pipeline/Handlers/DeferredCommandHandler.cs`)
- Serializes command for out-of-process dispatch
- Bundles dispatch payload
- Returns `false` (passive wait)

#### **GroupWaitHandler** (`Pipeline/Handlers/GroupWaitHandler.cs`)
- Unfolds composite layers
- Validates only IPassiveWait children
- Returns `false` (passive wait)

#### **SubWorkflowHandler** (`Pipeline/Handlers/SubWorkflowHandler.cs`)
- Extracts child workflow stream
- Drives initial entry loop for first child wait
- Cascades to appropriate child handler
- Returns child continuation outcome

#### **CompensationHandler** (`Pipeline/Handlers/CompensationHandler.cs`)
- Queries command history
- Sorts operations in LIFO order
- Invokes undo delegates
- Returns `true` (active wait, continue loop)

### 5. Factory Implementations

#### **EvaluatorFactory** (`Pipeline/EvaluatorFactory.cs`)
- Creates and caches stateless evaluators
- Routes based on DTO type (SignalWaitDto, TimeWaitDto, etc.)
- Uses pattern matching for dispatch

#### **HandlerFactory** (`Pipeline/HandlerFactory.cs`)
- Creates and caches stateless handlers
- Routes based on Wait type (ISignalWait, TimeWait, etc.)
- Handles lazy initialization for SubWorkflowHandler (circular dependency)

### 6. Refactored Runner

#### **RefactoredWorkflowRunner** (`Pipeline/RefactoredWorkflowRunner.cs`)
- Clean pipeline coordinator
- Execution flow:
  1. Create execution context from request
  2. Run evaluator (abort if fails)
  3. Enter execution loop:
     - Advance state machine
     - Route to appropriate handler
     - Process cancellations
     - Continue or suspend based on handler result
  4. Map context to result DTO
  5. Send to orchestrator

## Status: Phase 1 Complete

### ✅ Completed
- All core interfaces defined
- All evaluators implemented (structure in place)
- All handlers implemented (structure in place)
- Factory implementations complete
- RefactoredWorkflowRunner implemented
- Build successful

### 🚧 TODO (Future Phases)

#### Evaluators
- [ ] Move signal match compilation logic from old WorkflowRunner
- [ ] Implement GroupWait composite evaluation logic
- [ ] Add parent dependency checking (GroupWait children)

#### Handlers
- [ ] Implement MatchExpressionTransformer integration in SignalWaitHandler
- [ ] Add template index updates
- [ ] Implement TimeWait scheduling calculations
- [ ] Complete ImmediateCommandHandler execution logic
- [ ] Add command history tracking for compensation
- [ ] Implement DeferredCommandHandler serialization
- [ ] Complete GroupWaitHandler validation logic
- [ ] Fix SubWorkflowHandler cascading (currently simplified)
- [ ] Implement CompensationHandler LIFO execution

#### Integration
- [ ] Wire up RefactoredWorkflowRunner in DI container
- [ ] Add IWorkflowRunnerClient result sending logic
- [ ] Handle sub-workflow completion and parent resumption
- [ ] Migrate old WorkflowRunner logic incrementally
- [ ] Add comprehensive unit tests
- [ ] Integration testing

#### Cancellation
- [ ] Complete CancelHandler implementation
- [ ] Add token matching logic
- [ ] Implement sub-tree pruning
- [ ] Test callback execution

## Migration Strategy

The old `WorkflowRunner` class remains intact. The new pipeline is isolated in the `Pipeline/` folder. This allows for:

1. **Gradual Migration**: Logic can be moved piece by piece
2. **Side-by-Side Testing**: Both implementations can run in parallel
3. **Safe Rollback**: Original implementation preserved
4. **Feature Parity Verification**: Compare outputs before full switchover

## Key Design Principles

1. **Stateless Components**: All evaluators and handlers are stateless and thread-safe
2. **Single Responsibility**: Each component has one clear purpose
3. **Open for Extension**: New wait types can add evaluators/handlers easily
4. **Testability**: Small, focused classes with clear interfaces
5. **Pipeline Clarity**: Explicit evaluation → advancement → handling flow

## Next Steps

1. Register RefactoredWorkflowRunner in DI container
2. Add feature flag to switch between old and new runners
3. Begin migrating complex logic (signal matching, compensation)
4. Add comprehensive unit tests for each component
5. Run integration tests comparing old vs new behavior
6. Gradually deprecate old WorkflowRunner
