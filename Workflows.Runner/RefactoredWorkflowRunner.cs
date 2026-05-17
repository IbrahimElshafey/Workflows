using System;
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
    internal class RefactoredWorkflowRunner : IWorkflowRunner
    {
        private readonly WorkflowStateService _stateService;
        private readonly MatcherFactory _matcherFactory;
        private readonly ProcessorFactory _processorFactory;
        private readonly CancelProcessor _cancelHandler;
        private readonly StateMachineAdvancer _stateMachineAdvancer;
        private readonly IWorkflowRunnerClient _resultSender;

        public RefactoredWorkflowRunner(
            WorkflowStateService stateService,
            MatcherFactory matcherFactory,
            ProcessorFactory processorFactory,
            CancelProcessor cancelHandler,
            StateMachineAdvancer stateMachineAdvancer,
            IWorkflowRunnerClient resultSender)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _matcherFactory = matcherFactory ?? throw new ArgumentNullException(nameof(matcherFactory));
            _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
            _cancelHandler = cancelHandler ?? throw new ArgumentNullException(nameof(cancelHandler));
            _stateMachineAdvancer = stateMachineAdvancer ?? throw new ArgumentNullException(nameof(stateMachineAdvancer));
            _resultSender = resultSender ?? throw new ArgumentNullException(nameof(resultSender));
        }

        public async Task<AsyncResult> RunWorkflowAsync(WorkflowExecutionRequest incomingContext)
        {
            // 1. Isolate and deserialize state into a clean context
            var context = _stateService.CreateExecutionContext(incomingContext);

            // Get the triggering wait DTO
            var triggeringWaitDto = _stateService.FindWaitById(
                context.WorkflowState.Waits, 
                context.TriggeringWaitId);

            if (triggeringWaitDto == null)
            {
                return new AsyncResult(
                    Guid.NewGuid(),
                    null,
                    "Error",
                    $"Triggering wait with ID {context.TriggeringWaitId} not found.",
                    DateTime.UtcNow);
            }

            // 2. Incoming Matching Phase
            var matcher = _matcherFactory.GetMatcher(triggeringWaitDto);

            // If matching fails or forms a partial match, exit immediately
            bool shouldProceed = await matcher.MatchAsync(triggeringWaitDto);
            if (!shouldProceed)
            {
                // Return error result
                return new AsyncResult(
                    Guid.NewGuid(),
                    null,
                    "Rejected",
                    "Matching failed or partial match.",
                    DateTime.UtcNow);
            }

            context.ContinueExecutionLoop = true;

            // 3. Execution Cycle Loop
            while (context.ContinueExecutionLoop)
            {
                // Get current state object (could be main or child sub-workflow state)
                var currentState = context.WorkflowState.StateObject;

                // Advance the underlying C# state machine
                var advancerResult = await _stateMachineAdvancer.RunAsync(context.WorkflowStream, currentState);

                Definition.Wait yieldedWait = advancerResult?.Wait;

                if (yieldedWait == null) // End of stream/workflow completion
                {
                    // Workflow completed - set status
                    context.WorkflowState.Status = Abstraction.Enums.WorkflowInstanceStatus.Completed;
                    break;
                }

                // Update state object
                context.WorkflowState.StateObject = advancerResult?.State;

                // Check if this wait should be cancelled and skipped
                bool wasCancelled = await _cancelHandler.CheckAndSkipCancelledWaitAsync(yieldedWait, context);
                if (wasCancelled)
                {
                    // Skip this wait and continue to next iteration
                    context.ContinueExecutionLoop = true;
                    continue;
                }

                // Route to specific outgoing wait processor
                var processor = _processorFactory.GetProcessor(yieldedWait);
                context.ContinueExecutionLoop = await processor.ProcessAsync(yieldedWait, context);

                // Execute interruption logic and trigger attached OnCancel callbacks
                await _cancelHandler.ProcessCancellationsWithCallbacksAsync(context);
            }

            // 4. Send updated snapshot back to Orchestrator to persist
            var runResultDto = _stateService.MapToResultDto(context);

            // Note: IWorkflowRunnerClient.SendWorkflowRunResultAsync requires WorkflowExecutionResponse            // For now, we'll return the result directly. The client integration will be handled separately.
            // await _resultSender.SendWorkflowRunResultAsync(runResultDto, new WorkflowExecutionResponse { ... });

            return runResultDto;
        }
    }
}
