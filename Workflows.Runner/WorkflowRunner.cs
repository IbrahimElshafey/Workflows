using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;
using Workflows.Runner.Pipeline;
using Workflows.Runner.Pipeline.Matchers;
using Workflows.Runner.Pipeline.Processors;

namespace Workflows.Runner
{
    /// <summary>
    /// Refactored stateless workflow runner implementation.
    /// Uses a two-phase pipeline: Matchers validate incoming events, Processors handle yielded waits.
    /// </summary>
    internal class WorkflowRunner : IWorkflowRunner
    {
        private readonly WorkflowStateService _stateService;
        private readonly MatcherFactory _matcherFactory;
        private readonly ProcessorFactory _processorFactory;
        private readonly CancelProcessor _cancelHandler;
        private readonly StateMachineAdvancer _stateMachineAdvancer;
        private readonly IWorkflowRunnerClient _resultSender;
        private readonly WorkflowExecutionContext _context;

        public WorkflowRunner(
            WorkflowStateService stateService,
            MatcherFactory matcherFactory,
            ProcessorFactory processorFactory,
            CancelProcessor cancelHandler,
            StateMachineAdvancer stateMachineAdvancer,
            IWorkflowRunnerClient resultSender,
            WorkflowExecutionContext context)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _matcherFactory = matcherFactory ?? throw new ArgumentNullException(nameof(matcherFactory));
            _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
            _cancelHandler = cancelHandler ?? throw new ArgumentNullException(nameof(cancelHandler));
            _stateMachineAdvancer = stateMachineAdvancer ?? throw new ArgumentNullException(nameof(stateMachineAdvancer));
            _resultSender = resultSender ?? throw new ArgumentNullException(nameof(resultSender));
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<AsyncResult> RunWorkflowAsync(WorkflowExecutionRequest incomingContext)
        {
            // 1. Isolate and deserialize state into a clean context
            _stateService.PopulateExecutionContext(_context, incomingContext);

            bool shouldProceed = true;
            if (_context.TriggeringWaitId != Guid.Empty)
            {
                // Get the triggering wait DTO
                var triggeringWaitDto = _stateService.FindWaitById(
                    _context.WorkflowState.Waits, 
                    _context.TriggeringWaitId);

                if (triggeringWaitDto == null)
                {
                    return new AsyncResult(
                        Guid.NewGuid(),
                        null,
                        "Error",
                        $"Triggering wait with ID {_context.TriggeringWaitId} not found.",
                        DateTime.UtcNow);
                }

                // 2. Incoming Matching Phase
                var matcher = _matcherFactory.GetMatcher(triggeringWaitDto);

                // If matching fails or forms a partial match, exit immediately
                shouldProceed = await matcher.MatchAsync(triggeringWaitDto);
            }

            if (!shouldProceed)
            {
                // If it is a partial match (the triggering wait succeeded but overall match failed),
                // we still need to persist the updated wait statuses.
                if (_context.TriggeringWaitId != Guid.Empty)
                {
                    var triggeringWaitDto = _stateService.FindWaitById(
                        _context.WorkflowState.Waits,
                        _context.TriggeringWaitId);

                    if (triggeringWaitDto != null && (triggeringWaitDto.Status == Abstraction.Enums.WaitStatus.Completed || triggeringWaitDto.Status == Abstraction.Enums.WaitStatus.Matched))
                    {
                        return await SendResultAsync(_context);
                    }
                }

                // Return error result
                return new AsyncResult(
                    Guid.NewGuid(),
                    null,
                    "Unmatched",
                    "Matching failed or partial match.",
                    DateTime.UtcNow);
            }

            _context.ContinueExecutionLoop = true;

            // 3. Execution Cycle Loop
            while (_context.ContinueExecutionLoop)
            {
                // Advance the underlying C# state machine
                var advancerResult = await _stateMachineAdvancer.RunAsync(
                    _context.WorkflowStream,
                    _context.WorkflowState.StateObject);

                Definition.Wait yieldedWait = advancerResult?.Wait;

                if (yieldedWait == null)
                {
                    _context.WorkflowState.Status = Abstraction.Enums.WorkflowInstanceStatus.Completed;
                    break;
                }

                _context.WorkflowState.StateObject = advancerResult.State;

                // Check if this wait should be cancelled and skipped
                bool wasCancelled = await _cancelHandler.CheckAndSkipCancelledWaitAsync(yieldedWait, _context);
                if (wasCancelled)
                {
                    _context.ContinueExecutionLoop = true;
                    continue;
                }

                // Route to specific outgoing wait processor
                var processor = _processorFactory.GetProcessor(yieldedWait);
                _context.ContinueExecutionLoop = await processor.ProcessAsync(yieldedWait, _context);

                // Execute interruption logic and trigger attached OnCancel callbacks
                await _cancelHandler.ProcessCancellationsWithCallbacksAsync(_context);
            }

            // 4. Send updated snapshot back to Orchestrator to persist
            return await SendResultAsync(_context);
        }

        public async Task<AsyncResult> StartWorkflow(string workflowName, object input = null)
        {
            if (string.IsNullOrWhiteSpace(workflowName))
                throw new ArgumentNullException(nameof(workflowName));

            // 1. Create a fresh workflow state and execution context for a new instance
            _stateService.PopulateNewWorkflowContext(_context, workflowName, input);
            _context.WorkflowState.Status = Abstraction.Enums.WorkflowInstanceStatus.Running;

            _context.ContinueExecutionLoop = true;

            // 2. Execution Cycle Loop (same as RunWorkflowAsync, but no matching phase)
            while (_context.ContinueExecutionLoop)
            {
                var advancerResult = await _stateMachineAdvancer.RunAsync(
                    _context.WorkflowStream,
                    _context.WorkflowState.StateObject);

                Definition.Wait yieldedWait = advancerResult?.Wait;

                if (yieldedWait == null)
                {
                    _context.WorkflowState.Status = Abstraction.Enums.WorkflowInstanceStatus.Completed;
                    break;
                }

                _context.WorkflowState.StateObject = advancerResult.State;

                bool wasCancelled = await _cancelHandler.CheckAndSkipCancelledWaitAsync(yieldedWait, _context);
                if (wasCancelled)
                {
                    _context.ContinueExecutionLoop = true;
                    continue;
                }

                var processor = _processorFactory.GetProcessor(yieldedWait);
                _context.ContinueExecutionLoop = await processor.ProcessAsync(yieldedWait, _context);

                await _cancelHandler.ProcessCancellationsWithCallbacksAsync(_context);
            }

            // 3. Send updated snapshot back to Orchestrator to persist
            return await SendResultAsync(_context);
        }

        private async Task<AsyncResult> SendResultAsync(WorkflowExecutionContext context)
        {
            var runResult = _stateService.MapToResultDto(context);
            var response = new WorkflowExecutionResponse
            {
                UpdatedState = context.WorkflowState,
                ConsumedWaitsIds = context.ConsumedWaitsIds
            };
            return await _resultSender.SendWorkflowRunResultAsync(runResult, response);
        }
    }
}
