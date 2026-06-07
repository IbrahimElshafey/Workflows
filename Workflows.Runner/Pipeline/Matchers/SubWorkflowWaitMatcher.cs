using FastExpressionCompiler;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Runner;
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
        private readonly ConcurrentDictionary<string, Func<object, object>> _workflowInvokers = new();
        private readonly WorkflowExecutionContext _context;
        private readonly MatcherFactory _matcherFactory;
        private readonly StateMachineAdvancer _stateMachineAdvancer;
        private readonly ProcessorFactory _processorFactory;
        private readonly CancelProcessor _cancelProcessor;
        private readonly IWorkflowRegistry _workflowRegistry;

        public SubWorkflowWaitMatcher(
            WorkflowExecutionContext context,
            MatcherFactory matcherFactory,
            StateMachineAdvancer stateMachineAdvancer,
            ProcessorFactory processorFactory,
            CancelProcessor cancelProcessor,
            IWorkflowRegistry workflowRegistry)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _matcherFactory = matcherFactory ?? throw new ArgumentNullException(nameof(matcherFactory));
            _stateMachineAdvancer = stateMachineAdvancer ?? throw new ArgumentNullException(nameof(stateMachineAdvancer));
            _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
            _cancelProcessor = cancelProcessor ?? throw new ArgumentNullException(nameof(cancelProcessor));
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
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

            // Use the stable StateMachineObjectId from the DTO as the key
            var childKey = subWorkflowWaitDto.StateMachineObjectId.ToString();

            // Restore or create child WorkflowStateObject from the unified StateMachinesObjects bag
            if (!_context.WorkflowState.StateObject.StateMachinesObjects.TryGetValue(childKey, out var childRaw)
                || childRaw is not WorkflowStateObject childState)
            {
                childState = new WorkflowStateObject();
            }

            // Execute the sub-workflow to completion using the CallerName from the DTO
            var callerName = string.IsNullOrEmpty(subWorkflowWaitDto.CallerName) ? "Run" : subWorkflowWaitDto.CallerName;
            var workflowInvoker = GetOrAddWorkflowInvoker(workflowTypes.WorkflowContainer, callerName);
            var subWorkflowStream = (System.Collections.Generic.IAsyncEnumerable<Definition.Wait>)workflowInvoker(_context.WorkflowInstance);
            bool subWorkflowCompleted = false;

            while (!subWorkflowCompleted)
            {
                var advancerResult = await _stateMachineAdvancer.RunAsync(subWorkflowStream, childState);
                var yieldedWait = advancerResult?.Wait;

                if (yieldedWait == null)
                {
                    subWorkflowCompleted = true;
                    break;
                }

                childState = advancerResult.State;

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
                    // Sub-workflow suspended — store full WorkflowStateObject under its stable key
                    _context.WorkflowState.StateObject.StateMachinesObjects[childKey] = childState;
                    return false;
                }
            }

            // Sub-workflow completed successfully
            subWorkflowWaitDto.Status = WaitStatus.Completed;

            // Remove child state since sub-workflow is done
            _context.WorkflowState.StateObject.StateMachinesObjects.Remove(childKey);

            // Restore parent stream on context
            var parentCallerName = "Run";
            var parentSub = FindParentSubWorkflow(subWorkflowWaitDto, _context.WorkflowState.Waits);
            if (parentSub != null)
            {
                parentCallerName = string.IsNullOrEmpty(parentSub.CallerName) ? "Run" : parentSub.CallerName;
            }
            var parentInvoker = GetOrAddWorkflowInvoker(workflowTypes.WorkflowContainer, parentCallerName);
            _context.WorkflowStream = (System.Collections.Generic.IAsyncEnumerable<Definition.Wait>)parentInvoker(_context.WorkflowInstance);

            // Propagate matching to parent wait if present (e.g., GroupWait containing this sub-workflow)
            if (subWorkflowWaitDto.ParentWaitId.HasValue)
            {
                return await MatchParentAsync(subWorkflowWaitDto.ParentWaitId.Value, _context, _matcherFactory);
            }

            return true;
        }

        private SubWorkflowWaitDto FindParentSubWorkflow(WaitInfrastructureDto wait, System.Collections.Generic.List<WaitInfrastructureDto> waits)
        {
            if (wait.ParentWaitId == null) return null;
            var parent = FindWaitById(waits, wait.ParentWaitId.Value);
            if (parent == null) return null;
            if (parent is SubWorkflowWaitDto parentSub) return parentSub;
            return FindParentSubWorkflow(parent, waits);
        }

        private WaitInfrastructureDto FindWaitById(System.Collections.Generic.IEnumerable<WaitInfrastructureDto> waits, Guid id)
        {
            if (waits == null) return null;
            var stack = new System.Collections.Generic.Stack<WaitInfrastructureDto>(waits.Where(w => w != null));
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
        private Func<object, object> GetOrAddWorkflowInvoker(Type containerType, string methodName)
        {
            var key = $"{containerType.FullName}:{methodName}";
            return _workflowInvokers.GetOrAdd(key, _ =>
            {
                var method = containerType.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null) ?? containerType.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null) return null;

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var call = Expression.Call(Expression.Convert(instanceParam, containerType), method);
                var lambda = Expression.Lambda<Func<object, object>>(Expression.Convert(call, typeof(object)), instanceParam);
                return lambda.CompileFast();
            });
        }
    }
}
