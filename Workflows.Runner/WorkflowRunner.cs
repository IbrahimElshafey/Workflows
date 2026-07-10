using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;
using Workflows.Runner.Pipeline;
using Workflows.Runner.Pipeline.CompletionChecker;
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
        private readonly CompletionCheckerFactory _matcherFactory;
        private readonly ProcessorFactory _processorFactory;
        private readonly CancelProcessor _cancelHandler;
        private readonly StateMachineAdvancer _stateMachineAdvancer;
        private readonly IWorkflowRunnerClient _resultSender;
        private readonly WorkflowExecutionContext _context;

        public WorkflowRunner(
            WorkflowStateService stateService,
            CompletionCheckerFactory matcherFactory,
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

            // If no triggering wait, run the root execution loop directly
            if (_context.TriggeringWaitId == Guid.Empty)
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
            var checker = _matcherFactory.GetChecker(triggeringWaitDto);
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
                var parentChecker = _matcherFactory.GetChecker(targetNode);
                if (!await parentChecker.IsCompleted(targetNode))
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
            if (string.IsNullOrWhiteSpace(workflowName))
                throw new ArgumentNullException(nameof(workflowName));

            // Create a fresh workflow state and execution context
            _stateService.PopulateNewWorkflowContext(_context, workflowName, input);
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
            if (!wait.ParentWaitId.HasValue)
                return null;

            return _stateService.FindWaitById(
                _context.WorkflowState.Waits,
                wait.ParentWaitId.Value);
        }

        /// <summary>
        /// Runs the core execution loop: advances the state machine, processes yielded waits,
        /// handles cancellations, and sends the result for persistence.
        /// Shared between RunWorkflowAsync (after matching) and StartWorkflow.
        /// </summary>
        private async Task<AsyncResult> RunExecutionLoopAndSendResult()
        {
            _context.ContinueExecutionLoop = true;

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

                ValidateExecutionWait(yieldedWait, _context.WorkflowState.Waits, _context.WorkflowState.WorkflowType);

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


        private void ValidateExecutionWait(
            Definition.Wait yieldedWait,
            List<Abstraction.DTOs.Waits.WaitInfrastructureDto> existingWaits,
            string workflowType)
        {
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (existingWaits != null)
            {
                foreach (var existing in existingWaits)
                {
                    CollectActiveWaitNames(existing, seenNames);
                }
            }

            ValidateWaitTreeRecursive(yieldedWait, seenNames, workflowType);
        }

        private void CollectActiveWaitNames(
            Abstraction.DTOs.Waits.WaitInfrastructureDto waitDto,
            HashSet<string> seenNames)
        {
            if (waitDto == null) return;
            if (!string.IsNullOrWhiteSpace(waitDto.WaitName))
            {
                seenNames.Add(waitDto.WaitName);
            }
            if (waitDto.ChildWaits != null)
            {
                foreach (var child in waitDto.ChildWaits)
                {
                    CollectActiveWaitNames(child, seenNames);
                }
            }
        }

        private void ValidateWaitTreeRecursive(
            Definition.Wait wait,
            HashSet<string> seenNames,
            string workflowType)
        {
            if (wait == null) return;

            var name = wait.WaitName;
            if (wait is Definition.CompensationWait compWait)
            {
                name = compWait.Token;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException($"Wait name is mandatory. A wait of type '{wait.GetType().Name}' in workflow '{workflowType}' is defined without a name.");
            }

            if (!seenNames.Add(name))
            {
                throw new InvalidOperationException($"Wait name '{name}' is duplicate in workflow '{workflowType}'. Wait names must be unique within a workflow.");
            }

            if (wait.ChildWaits != null)
            {
                foreach (var child in wait.ChildWaits)
                {
                    ValidateWaitTreeRecursive(child, seenNames, workflowType);
                }
            }
        }
    }
}
