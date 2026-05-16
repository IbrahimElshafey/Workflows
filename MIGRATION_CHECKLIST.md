# Workflow Runner Refactoring - Migration Checklist

## Phase 1: Foundation ✅ COMPLETE

- [x] Create pipeline folder structure
- [x] Define core interfaces (IWorkflowWaitEvaluator, IWorkflowWaitHandler)
- [x] Create WorkflowExecutionContext
- [x] Implement factory interfaces and implementations
- [x] Create WorkflowStateService
- [x] Create all evaluator classes (structure)
- [x] Create all handler classes (structure)
- [x] Create CancelHandler (structure)
- [x] Create RefactoredWorkflowRunner
- [x] Add DI registration extensions
- [x] Build successful
- [x] Write comprehensive documentation

## Phase 2: Core Logic Migration 🚧 TODO

### Evaluators

#### SignalWaitEvaluator
- [ ] Move CompileMatch logic from old WorkflowRunner (line ~414)
- [ ] Implement expression compilation
- [ ] Add parent GroupWait dependency checking
- [ ] Test signal matching edge cases
- [ ] Test partial match handling

#### TimeWaitEvaluator
- [ ] Add timer boundary validation logic (if needed)
- [ ] Test with various time scenarios

#### DeferredCommandEvaluator
- [ ] Test with various command result types
- [ ] Add error handling for OnResultAction failures
- [ ] Implement OnFailureAction logic

#### GroupWaitEvaluator
- [ ] Implement MatchAll logic
- [ ] Implement MatchAny logic
- [ ] Add branch pruning on fulfillment
- [ ] Test nested group scenarios
- [ ] Test with cancelled child waits

### Handlers

#### SignalWaitHandler
- [ ] Integrate MatchExpressionTransformer
- [ ] Implement template index updates
- [ ] Move SaveWaitStatesToMachineState logic completely
- [ ] Test with various signal types
- [ ] Test template caching

#### TimeWaitHandler
- [ ] Implement absolute datetime calculation
- [ ] Add schedule registration logic
- [ ] Test with various time offsets
- [ ] Test timezone handling

#### ImmediateCommandHandler
- [ ] Implement handler resolution from factory
- [ ] Execute command through handler
- [ ] Capture result and invoke OnResultAction
- [ ] Track command for compensation
- [ ] Add error handling and OnFailure invocation
- [ ] Test with mock handlers
- [ ] Test compensation tracking

#### DeferredCommandHandler
- [ ] Implement command serialization
- [ ] Bundle dispatch payload
- [ ] Track for compensation
- [ ] Test with various command types

#### GroupWaitHandler
- [ ] Unfold composite layers recursively
- [ ] Validate IPassiveWait-only children
- [ ] Throw on active wait nesting
- [ ] Test with mixed wait types
- [ ] Test deep nesting scenarios

#### SubWorkflowHandler
- [ ] Implement proper handler cascading
- [ ] Fix child context creation issue
- [ ] Handle child wait properly in parent context
- [ ] Test with nested sub-workflows
- [ ] Test sub-workflow completion scenarios

#### CompensationHandler
- [ ] Implement BuildCommandHistory access
- [ ] Sort history in LIFO order
- [ ] Invoke compensation delegates
- [ ] Mark commands as compensated
- [ ] Test with various command sequences
- [ ] Test partial compensation scenarios

### Core Components

#### CancelHandler
- [ ] Implement token matching against waits
- [ ] Execute OnCancel callbacks
- [ ] Prune cancelled sub-trees
- [ ] Fast-forward execution index
- [ ] Test with various cancellation scenarios
- [ ] Test with nested cancelled waits

#### RefactoredWorkflowRunner
- [ ] Implement sub-workflow completion handling
- [ ] Resume parent workflow after child completion
- [ ] Fix IWorkflowRunnerClient.SendAsync integration
- [ ] Handle workflow completion edge cases
- [ ] Test complete execution cycles
- [ ] Test error scenarios

#### WorkflowStateService
- [ ] Validate all edge cases in context creation
- [ ] Test with corrupted state
- [ ] Test with missing parent sub-workflows
- [ ] Optimize result DTO mapping

## Phase 3: Integration & Testing 🔮 FUTURE

### Unit Tests
- [ ] EvaluatorFactory tests
- [ ] HandlerFactory tests
- [ ] SignalWaitEvaluator tests (with mocks)
- [ ] TimeWaitEvaluator tests
- [ ] DeferredCommandEvaluator tests
- [ ] GroupWaitEvaluator tests
- [ ] All handler tests
- [ ] CancelHandler tests
- [ ] WorkflowStateService tests
- [ ] RefactoredWorkflowRunner tests

### Integration Tests
- [ ] Compare old vs new runner outputs
- [ ] Test complete workflow scenarios
- [ ] Test with real workflow samples
- [ ] Performance benchmarks (old vs new)
- [ ] Memory usage comparison
- [ ] Concurrency tests

### DI Integration
- [ ] Register in actual application
- [ ] Add feature flag for runtime switching
- [ ] Create migration scripts if needed
- [ ] Update documentation

## Phase 4: Production Readiness 🔮 FUTURE

### Monitoring & Observability
- [ ] Add logging throughout pipeline
- [ ] Add performance metrics
- [ ] Add execution tracing
- [ ] Add error reporting
- [ ] Create dashboard

### Documentation
- [ ] API documentation
- [ ] Migration guide for teams
- [ ] Troubleshooting guide
- [ ] Performance tuning guide

### Deployment
- [ ] Canary deployment strategy
- [ ] Rollback procedure
- [ ] Monitoring alerts
- [ ] Success criteria definition

## Phase 5: Cleanup 🔮 FUTURE

- [ ] Remove old WorkflowRunner class
- [ ] Remove dead code
- [ ] Update all references
- [ ] Archive old tests
- [ ] Final documentation update
- [ ] Announce completion

---

## Current Status

**Phase**: 1 (Foundation)  
**Status**: ✅ Complete  
**Next Action**: Begin Phase 2 - Choose first component to migrate logic  
**Build**: ✅ Successful  
**Tests**: ⏳ Not Started

**Recommendation**: Start with **SignalWaitEvaluator** logic migration as it's the most complex and commonly used.
