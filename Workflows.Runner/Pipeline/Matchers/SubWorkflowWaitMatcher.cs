using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Runner;
using Workflows.Runner.Cache;
using Workflows.Runner.Pipeline.Processors;

namespace Workflows.Runner.Pipeline.Matchers
{
    /// <summary>
    /// Matcher for SubWorkflowWait - executes the sub-workflow to completion.
    /// This is a special matcher that actually runs the sub-workflow when triggered,
    /// rather than just validating an incoming event.
    /// </summary>
    internal class SubWorkflowWaitMatcher : WorkflowWaitMatcher
    {
        private readonly WorkflowExecutionContext _context;
        private readonly MatcherFactory _matcherFactory;
        private readonly StateMachineAdvancer _stateMachineAdvancer;
        private readonly ProcessorFactory _processorFactory;
        private readonly CancelProcessor _cancelProcessor;
        private readonly IWorkflowRegistry _workflowRegistry;
        private readonly WorkflowTemplateCache _templateCache;

        public SubWorkflowWaitMatcher(
            WorkflowExecutionContext context,
            MatcherFactory matcherFactory,
            StateMachineAdvancer stateMachineAdvancer,
            ProcessorFactory processorFactory,
            CancelProcessor cancelProcessor,
            IWorkflowRegistry workflowRegistry,
            WorkflowTemplateCache templateCache)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _matcherFactory = matcherFactory ?? throw new ArgumentNullException(nameof(matcherFactory));
            _stateMachineAdvancer = stateMachineAdvancer ?? throw new ArgumentNullException(nameof(stateMachineAdvancer));
            _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
            _cancelProcessor = cancelProcessor ?? throw new ArgumentNullException(nameof(cancelProcessor));
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
            _templateCache = templateCache ?? throw new ArgumentNullException(nameof(templateCache));
        }

        public override async Task<bool> MatchAsync(WaitInfrastructureDto waitDto)
        {
            var subWorkflowWaitDto = waitDto as SubWorkflowWaitDto;
            if (subWorkflowWaitDto == null)
            {
                throw new InvalidOperationException(
                    "SubWorkflowWaitMatcher requires a SubWorkflowWaitDto.");
            }

            // Get workflow types
            if (!_workflowRegistry.Workflows.TryGetValue(_context.WorkflowState.WorkflowType, out var workflowTypes))
            {
                throw new InvalidOperationException($"Workflow {_context.WorkflowState.WorkflowType} not registered.");
            }

            // Get or create child state
            if (!_context.WorkflowState.StateObject.StateMachinesObjects.TryGetValue(subWorkflowWaitDto.Id, out var storedChildState))
            {
                // First time - create new child state
                storedChildState = new WorkflowStateObject();
                _context.WorkflowState.StateObject.StateMachinesObjects[subWorkflowWaitDto.Id] = storedChildState;
            }

            var childState = storedChildState as WorkflowStateObject;
            if (childState == null)
            {
                throw new InvalidOperationException("Child state is not a WorkflowStateObject.");
            }

            // Execute the sub-workflow to completion using the CallerName from the DTO
            var callerName = string.IsNullOrEmpty(subWorkflowWaitDto.CallerName) ? "Run" : subWorkflowWaitDto.CallerName;
            var workflowInvoker = _templateCache.GetOrAddWorkflowInvoker(workflowTypes.WorkflowContainer, callerName);
            var subWorkflowStream = (System.Collections.Generic.IAsyncEnumerable<Definition.Wait>)workflowInvoker(_context.WorkflowInstance);
            bool subWorkflowCompleted = false;

            while (!subWorkflowCompleted)
            {
                var advancerResult = await _stateMachineAdvancer.RunAsync(subWorkflowStream, childState);
                var yieldedWait = advancerResult?.Wait;

                if (yieldedWait == null)
                {
                    // Sub-workflow completed
                    subWorkflowCompleted = true;
                    break;
                }

                childState = advancerResult?.State;

                // Check cancellation
                bool wasCancelled = await _cancelProcessor.CheckAndSkipCancelledWaitAsync(yieldedWait, _context);
                if (wasCancelled)
                {
                    continue;
                }

                // Process the yielded wait
                var processor = _processorFactory.GetProcessor(yieldedWait);
                bool continueLoop = await processor.ProcessAsync(yieldedWait, _context);

                if (!continueLoop)
                {
                    // Sub-workflow suspended on a passive wait
                    // Store child state and exit
                    _context.WorkflowState.StateObject.StateMachinesObjects[subWorkflowWaitDto.Id] = childState;
                    return false; // Don't proceed - sub-workflow is waiting
                }
            }

            // Sub-workflow completed successfully
            subWorkflowWaitDto.Status = WaitStatus.Completed;

            // Remove child state since sub-workflow is done
            _context.WorkflowState.StateObject.StateMachinesObjects.Remove(subWorkflowWaitDto.Id);

            // Propagate matching to parent wait if present (e.g., GroupWait containing this sub-workflow)
            if (subWorkflowWaitDto.ParentWaitId.HasValue)
            {
                return await MatchParentAsync(subWorkflowWaitDto.ParentWaitId.Value, _context, _matcherFactory);
            }

            return true;
        }
    }
}
