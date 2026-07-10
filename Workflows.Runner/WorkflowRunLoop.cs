using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;
using Workflows.Abstraction.Runner;
using Workflows.Runner.Pipeline;
using Workflows.Runner.Pipeline.Processors;

namespace Workflows.Runner
{
    /// <summary>
    /// Result package containing execution outcome and final state snapshot.
    /// </summary>
    internal class ExecutionResult
    {
        public bool CompletedNatively { get; set; }
        public WorkflowStateObject FinalState { get; set; }
    }

    /// <summary>
    /// Shared service that executes a workflow stream and state machine state object.
    /// Handles advancement, wait validation, cancellation skipping, processor routing, and cancellation callback execution.
    /// </summary>
    internal class WorkflowRunLoop
    {
        private readonly StateMachineAdvancer _stateMachineAdvancer;
        private readonly CancelProcessor _cancelHandler;
        private readonly ProcessorFactory _processorFactory;
        private readonly WorkflowExecutionContext _context;
        private readonly IWorkflowRegistry _workflowRegistry;
        private readonly IWorkflowHydrator _hydrator;

        public WorkflowRunLoop(
            StateMachineAdvancer stateMachineAdvancer,
            CancelProcessor cancelHandler,
            ProcessorFactory processorFactory,
            WorkflowExecutionContext context,
            IWorkflowRegistry workflowRegistry,
            IWorkflowHydrator hydrator)
        {
            _stateMachineAdvancer = stateMachineAdvancer ?? throw new ArgumentNullException(nameof(stateMachineAdvancer));
            _cancelHandler = cancelHandler ?? throw new ArgumentNullException(nameof(cancelHandler));
            _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
            _hydrator = hydrator ?? throw new ArgumentNullException(nameof(hydrator));
        }

        /// <summary>
        /// Resumes/executes the given stream and returns the final state object and whether it completed natively.
        /// </summary>
        public async Task<ExecutionResult> ExecuteAsync(
            IAsyncEnumerable<Wait> stream,
            WorkflowStateObject stateObject)
        {
            _context.ContinueExecutionLoop = true;
            WorkflowStateObject currentState = stateObject;
            bool isRoot = ReferenceEquals(stateObject, _context.WorkflowState?.StateObject);

            while (_context.ContinueExecutionLoop)
            {
                // Advance the underlying C# state machine
                var advancerResult = await _stateMachineAdvancer.RunAsync(stream, currentState);
                Wait yieldedWait = advancerResult?.Wait;

                if (yieldedWait == null)
                {
                    return new ExecutionResult { CompletedNatively = true, FinalState = currentState };
                }

                ValidateExecutionWait(yieldedWait, _context.WorkflowState.Waits, _context.WorkflowState.WorkflowType);

                currentState = advancerResult.State;
                if (isRoot)
                {
                    _context.WorkflowState.StateObject = currentState;
                }

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

            return new ExecutionResult { CompletedNatively = false, FinalState = currentState };
        }

        /// <summary>
        /// Resumes execution of a sub-workflow, runs it to completion or suspension, and handles child state mapping/parent stream restoration.
        /// </summary>
        public async Task ResumeSubWorkflowAsync(SubWorkflowWaitDto subWorkflowWaitDto)
        {
            // Get workflow types
            if (!_workflowRegistry.Workflows.TryGetValue(_context.WorkflowState.WorkflowType, out var workflowTypes))
            {
                throw new InvalidOperationException($"Workflow {_context.WorkflowState.WorkflowType} not registered.");
            }

            var childKey = subWorkflowWaitDto.StateMachineObjectId.ToString();

            // Restore or create child WorkflowStateObject from the unified Locals bag
            if (!_context.WorkflowState.StateObject.Locals.TryGetValue(childKey, out var childRaw)
                || childRaw is not WorkflowStateObject childState)
            {
                childState = new WorkflowStateObject();
                _context.WorkflowState.StateObject.Locals[childKey] = childState;
            }

            var callerName = string.IsNullOrEmpty(subWorkflowWaitDto.CallerName) ? "Run" : subWorkflowWaitDto.CallerName;
            
            // Resolve sub-workflow parameter type dynamically from method signature
            var methodInfo = workflowTypes.WorkflowContainer.GetMethod(
                callerName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

            Type subStateType = typeof(object);
            if (methodInfo != null && methodInfo.GetParameters().Length == 1)
            {
                subStateType = methodInfo.GetParameters()[0].ParameterType;
            }

            if (!childState.Locals.TryGetValue("state", out var childStateObj) || childStateObj == null)
            {
                if (subStateType != typeof(object))
                {
                    childStateObj = Activator.CreateInstance(subStateType);
                    childState.Locals["state"] = childStateObj;
                }
            }

            var subWorkflowInvoker = _hydrator.GetInvoker(workflowTypes.WorkflowContainer, callerName, subStateType);
            var subWorkflowStream = (IAsyncEnumerable<Wait>)subWorkflowInvoker(_context.WorkflowInstance, childStateObj);

            // Execute using the unified loop
            var result = await ExecuteAsync(subWorkflowStream, childState);

            if (!result.CompletedNatively)
            {
                // Sub-workflow suspended — store full WorkflowStateObject under its stable key
                _context.WorkflowState.StateObject.Locals[childKey] = result.FinalState;
                return;
            }

            // Sub-workflow completed successfully
            subWorkflowWaitDto.Status = Abstraction.Enums.WaitStatus.Completed;

            // Remove child state since sub-workflow is done
            _context.WorkflowState.StateObject.Locals.Remove(childKey);

            // Restore parent stream on context
            Type parentStateType;
            string parentMethodName;
            var parentSub = FindParentSubWorkflow(subWorkflowWaitDto, _context.WorkflowState.Waits);
            if (parentSub != null)
            {
                parentMethodName = string.IsNullOrEmpty(parentSub.CallerName) ? "Run" : parentSub.CallerName;
                var parentMethodInfo = workflowTypes.WorkflowContainer.GetMethod(
                    parentMethodName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                parentStateType = typeof(object);
                if (parentMethodInfo != null && parentMethodInfo.GetParameters().Length == 1)
                {
                    parentStateType = parentMethodInfo.GetParameters()[0].ParameterType;
                }
            }
            else
            {
                parentMethodName = workflowTypes.StartMethod;
                parentStateType = workflowTypes.StateType;
            }

            var parentInvoker = _hydrator.GetInvoker(workflowTypes.WorkflowContainer, parentMethodName, parentStateType);
            object? parentStateParam = null;
            if (parentSub != null)
            {
                var parentKey = parentSub.StateMachineObjectId.ToString();
                if (_context.WorkflowState.StateObject.Locals.TryGetValue(parentKey, out var parentRaw)
                    && parentRaw is WorkflowStateObject parentState)
                {
                    parentState.Locals.TryGetValue("state", out parentStateParam);
                }
            }
            else
            {
                _context.WorkflowState.StateObject.Locals.TryGetValue("state", out parentStateParam);
            }
            _context.WorkflowStream = (IAsyncEnumerable<Wait>)parentInvoker(_context.WorkflowInstance, parentStateParam);
        }

        private SubWorkflowWaitDto FindParentSubWorkflow(WaitInfrastructureDto wait, List<WaitInfrastructureDto> waits)
        {
            if (wait.ParentWaitId == null) return null;
            var parent = FindWaitById(waits, wait.ParentWaitId.Value);
            if (parent == null) return null;
            if (parent is SubWorkflowWaitDto parentSub) return parentSub;
            return FindParentSubWorkflow(parent, waits);
        }

        private WaitInfrastructureDto FindWaitById(IEnumerable<WaitInfrastructureDto> waits, Guid id)
        {
            if (waits == null) return null;
            var stack = new Stack<WaitInfrastructureDto>(System.Linq.Enumerable.Where(waits, w => w != null));
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current.Id == id) return current;
                if (current.ChildWaits != null)
                {
                    foreach (var child in current.ChildWaits)
                    {
                        if (child != null) stack.Push(child);
                    }
                }
            }
            return null;
        }

        private void ValidateExecutionWait(
            Wait yieldedWait,
            List<WaitInfrastructureDto> existingWaits,
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
            WaitInfrastructureDto waitDto,
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
            Wait wait,
            HashSet<string> seenNames,
            string workflowType)
        {
            if (wait == null) return;

            var name = wait.WaitName;
            if (wait is CompensationWait compWait)
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
