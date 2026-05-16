using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;

namespace Workflows.Runner.Pipeline
{
    /// <summary>
    /// Refactored stateless workflow runner implementation.
    /// Uses a pipeline architecture with evaluators and handlers to process workflow execution.
    /// </summary>
    internal class RefactoredWorkflowRunner : IWorkflowRunner
    {
        private readonly WorkflowStateService _stateService;
        private readonly EvaluatorFactory _evaluatorFactory;
        private readonly HandlerFactory _handlerFactory;
        private readonly CancelHandler _cancelHandler;
        private readonly StateMachineAdvancer _stateMachineAdvancer;
        private readonly IWorkflowRunnerClient _resultSender;

        public RefactoredWorkflowRunner(
            WorkflowStateService stateService,
            EvaluatorFactory evaluatorFactory,
            HandlerFactory handlerFactory,
            CancelHandler cancelHandler,
            StateMachineAdvancer stateMachineAdvancer,
            IWorkflowRunnerClient resultSender)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _evaluatorFactory = evaluatorFactory ?? throw new ArgumentNullException(nameof(evaluatorFactory));
            _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
            _cancelHandler = cancelHandler ?? throw new ArgumentNullException(nameof(cancelHandler));
            _stateMachineAdvancer = stateMachineAdvancer ?? throw new ArgumentNullException(nameof(stateMachineAdvancer));
            _resultSender = resultSender ?? throw new ArgumentNullException(nameof(resultSender));
        }

        public async Task<AsyncResult> RunWorkflowAsync(WorkflowExecutionRequest incomingContext)
        {
            // 1. Isolate and deserialize state into a clean context
            var context = _stateService.CreateExecutionContext(incomingContext);

            // 2. Incoming Evaluation Phase
            var evaluator = _evaluatorFactory.GetEvaluator(context.TriggeringWaitDto);

            // If evaluation fails or forms a partial match, exit immediately
            bool shouldProceed = await evaluator.EvaluateAsync(context);
            if (!shouldProceed)
            {
                // Return error result
                return new AsyncResult(
                    Guid.NewGuid(),
                    null,
                    "Rejected",
                    "Evaluation failed or partial match.",
                    DateTime.UtcNow);
            }

            context.ContinueExecutionLoop = true;

            // 3. Execution Cycle Loop
            while (context.ContinueExecutionLoop)
            {
                // Advance the underlying C# state machine
                var advancerResult = await _stateMachineAdvancer.RunAsync(context.WorkflowStream, context.ActiveState);

                Definition.Wait yieldedWait = advancerResult?.Wait;

                if (yieldedWait == null) // End of stream/workflow completion
                {
                    // Check if this was a sub-workflow or main workflow
                    if (context.ParentSubWorkflow != null)
                    {
                        // Sub-workflow completed - resume parent workflow
                        await HandleSubWorkflowCompletionAsync(context, advancerResult);
                    }
                    else
                    {
                        // Main workflow completed
                        context.IsWorkflowCompleted = true;
                    }
                    break;
                }

                // Update active state
                context.ActiveState = advancerResult?.State;

                // Route over to specific outgoing wait handler
                var handler = _handlerFactory.GetHandler(yieldedWait);
                context.ContinueExecutionLoop = await handler.HandleAsync(yieldedWait, context);

                // Execute interruption logic and trigger attached OnCancel callbacks
                await _cancelHandler.ProcessCancellationsWithCallbacksAsync(context);
            }

            // 4. Send updated snapshot back to Orchestrator to persist
            var runResultDto = _stateService.MapToResultDto(context);

            // Note: IWorkflowRunnerClient.SendWorkflowRunResultAsync requires WorkflowExecutionResponse
            // For now, we'll return the result directly. The client integration will be handled separately.
            // await _resultSender.SendWorkflowRunResultAsync(runResultDto, new WorkflowExecutionResponse { ... });

            return runResultDto;
        }

        private async Task HandleSubWorkflowCompletionAsync(
            WorkflowExecutionContext context,
            DataObjects.AdvancerResult childAdvancerResult)
        {
            // Remove child state
            if (context.ActiveState.StateMachinesObjects?.ContainsKey(context.ParentSubWorkflow.Id) == true)
            {
                context.ActiveState.StateMachinesObjects.Remove(context.ParentSubWorkflow.Id);
            }

            // Resume parent workflow - need to get parent workflow stream
            // This requires access to the template cache to get the workflow invoker
            // For now, we'll mark this as TODO since it requires additional dependencies

            // TODO: Resume parent workflow after sub-workflow completion
            // This needs to be implemented when we have access to parent workflow stream
            context.IsWorkflowCompleted = true;
        }
    }
}
