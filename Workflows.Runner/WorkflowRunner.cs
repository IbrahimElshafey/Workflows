using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;
using Workflows.Runner.Pipeline;
using Workflows.Runner.Pipeline.CompletionChecker;

namespace Workflows.Runner
{
    /// <summary>
    /// Refactored stateless workflow runner implementation.
    /// Uses a two-phase pipeline: Matchers validate incoming events, Processors handle yielded waits.
    /// </summary>
    internal class WorkflowRunner : IWorkflowRunner
    {
        private readonly WorkflowStateService _stateService;
        private readonly CompletionCheckerFactory _completionChecker;
        private readonly IWorkflowRunnerClient _resultSender;
        private readonly WorkflowExecutionContext _context;
        private readonly WorkflowRunLoop workflowRunLoop;

        public WorkflowRunner(
            WorkflowStateService stateService,
            CompletionCheckerFactory completionChecker,
            IWorkflowRunnerClient resultSender,
            WorkflowExecutionContext context,
            WorkflowRunLoop workflowRunLoop)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _completionChecker = completionChecker ?? throw new ArgumentNullException(nameof(completionChecker));
            _resultSender = resultSender ?? throw new ArgumentNullException(nameof(resultSender));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            this.workflowRunLoop = workflowRunLoop ?? throw new ArgumentNullException(nameof(workflowRunLoop));
        }

        public async Task<AsyncResult> RunWorkflowAsync(WorkflowExecutionRequest incomingContext)
        {
            // 1. Isolate and deserialize state into a clean context
            _stateService.PopulateExecutionContext(_context, incomingContext);

            // If no triggering wait, run the root execution loop directly
            if (string.IsNullOrEmpty(_context.TriggeringWaitId))
            {
                return await RunExecutionLoopAndSendResult();
            }

            // 2. Get the incoming triggering wait
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

            // 3. Check the leaf wait itself (no parent traversal)
            var checker = _completionChecker.GetChecker(triggeringWaitDto);
            if (!await checker.IsCompleted(triggeringWaitDto))
            {
                // Leaf didn't match — check if it was at least partially matched
                if (triggeringWaitDto.Status == Abstraction.Enums.WaitStatus.Completed
                    || triggeringWaitDto.Status == Abstraction.Enums.WaitStatus.Matched)
                {
                    return await SendResultAsync(_context);
                }

                return new AsyncResult(
                    Guid.NewGuid(),
                    null,
                    "Unmatched",
                    "Matching failed or partial match.",
                    DateTime.UtcNow);
            }

            // 4. Bubble up the hierarchy
            var targetNode = FindParentWait(triggeringWaitDto);

            while (targetNode != null)
            {
                bool isCompleted;

                switch (targetNode)
                {
                    case Abstraction.DTOs.Waits.GroupWaitDto groupWaitDto:
                        var groupChecker = _completionChecker.GetChecker(groupWaitDto);
                        isCompleted = await groupChecker.IsCompleted(groupWaitDto);
                        break;

                    case Abstraction.DTOs.Waits.ExternalGroupWaitDto externalGroupWaitDto:
                        var externalGroupChecker = _completionChecker.GetChecker(externalGroupWaitDto);
                        isCompleted = await externalGroupChecker.IsCompleted(externalGroupWaitDto);
                        break;

                    case Abstraction.DTOs.Waits.SubWorkflowWaitDto subWorkflowWaitDto:
                        // Execute/resume the sub-workflow
                        await workflowRunLoop.ResumeSubWorkflowAsync(subWorkflowWaitDto);
                        isCompleted = subWorkflowWaitDto.Status == Abstraction.Enums.WaitStatus.Completed;
                        break;

                    default:
                        throw new InvalidOperationException($"Unknown parent wait type: {targetNode.GetType().Name}");
                }

                if (!isCompleted)
                {
                    // Parent not yet complete — persist partial progress and yield
                    return await SendResultAsync(_context);
                }

                targetNode = FindParentWait(targetNode);
            }

            // 5. All parents completed — run root execution loop
            return await RunExecutionLoopAndSendResult();
        }

        public async Task<AsyncResult> StartWorkflow(string workflowName, object input = null)
        {
            return await StartWorkflow(workflowName, version: 0, input);
        }

        public async Task<AsyncResult> StartWorkflow(string workflowName, int version, object input = null)
        {
            if (string.IsNullOrWhiteSpace(workflowName))
                throw new ArgumentNullException(nameof(workflowName));

            // Create a fresh workflow state and execution context
            _stateService.PopulateNewWorkflowContext(_context, workflowName, version, input);
            _context.WorkflowState.Status = Abstraction.Enums.WorkflowInstanceStatus.Running;

            return await RunExecutionLoopAndSendResult();
        }

        /// <summary>
        /// Finds the parent wait node of the given wait in the hierarchy.
        /// Returns null if the wait has no parent (top-level).
        /// </summary>
        private Abstraction.DTOs.Waits.WaitInfrastructureDto FindParentWait(
            Abstraction.DTOs.Waits.WaitInfrastructureDto wait)
        {
            if (string.IsNullOrEmpty(wait.ParentWaitId))
                return null;

            return _stateService.FindWaitById(
                _context.WorkflowState.Waits,
                wait.ParentWaitId);
        }

        private async Task<AsyncResult> RunExecutionLoopAndSendResult()
        {
            var result = await workflowRunLoop.ExecuteAsync(_context.WorkflowStream, _context.WorkflowState.StateObject);
            _context.WorkflowState.StateObject = result.FinalState;
            if (result.CompletedNatively)
            {
                _context.WorkflowState.Status = Abstraction.Enums.WorkflowInstanceStatus.Completed;
            }

            return await SendResultAsync(_context);
        }

        private async Task<AsyncResult> SendResultAsync(WorkflowExecutionContext context)
        {
            var runResult = _stateService.MapToResultDto(context);
            var response = new WorkflowExecutionResponse
            {
                UpdatedState = context.WorkflowState,
                ConsumedWaitsIds = context.ConsumedWaitsIds,
                TriggeringSignalId = context.Signal?.Id
            };

            return await _resultSender.SendWorkflowRunResultAsync(runResult, response);
        }
    }
}
