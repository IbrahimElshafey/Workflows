using System;
using System.Collections.Generic;
using System.Linq;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Runner;

namespace Workflows.Runner.Pipeline
{
    /// <summary>
    /// Manages workflow execution contexts and state mapping.
    /// Reflection and instance creation are delegated to <see cref="IWorkflowHydrator"/>.
    /// </summary>
    internal class WorkflowStateService
    {
        private readonly IWorkflowRegistry _workflowRegistry;
        private readonly IWorkflowHydrator _hydrator;
        private readonly Mapper _mapper;

        public WorkflowStateService(
            IWorkflowRegistry workflowRegistry,
            IWorkflowHydrator hydrator,
            Mapper mapper)
        {
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
            _hydrator = hydrator ?? throw new ArgumentNullException(nameof(hydrator));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        /// <summary>
        /// Populates an execution context from the incoming request.
        /// </summary>
        public void PopulateExecutionContext(WorkflowExecutionContext context, WorkflowExecutionRequest incomingRequest)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (incomingRequest == null) throw new ArgumentNullException(nameof(incomingRequest));
            if (incomingRequest.WorkflowState == null) throw new ArgumentException("WorkflowState is required.", nameof(incomingRequest));
            if (incomingRequest.WorkflowState.Waits == null) throw new ArgumentException("WorkflowState.Waits is required.", nameof(incomingRequest));

            var state = incomingRequest.WorkflowState;

            // Find the triggering wait
            Abstraction.DTOs.Waits.WaitInfrastructureDto? triggeringWaitDto = null;
            if (incomingRequest.TriggeringWaitId != Guid.Empty)
            {
                triggeringWaitDto = FindWaitById(state.Waits, incomingRequest.TriggeringWaitId);
                if (triggeringWaitDto == null)
                {
                    throw new InvalidOperationException($"Triggering wait with ID {incomingRequest.TriggeringWaitId} not found.");
                }

                if (triggeringWaitDto.Status != Abstraction.Enums.WaitStatus.Waiting && triggeringWaitDto.Status != Abstraction.Enums.WaitStatus.Matched)
                {
                    throw new InvalidOperationException("Triggering wait is not in Waiting or Matched status.");
                }
            }

            // Get workflow types
            if (!_workflowRegistry.Workflows.TryGetValue(state.WorkflowType, out var workflowTypes))
            {
                throw new InvalidOperationException($"Workflow {state.WorkflowType} not registered.");
            }

            // Create or reuse workflow instance
            if (state.StateObject == null)
            {
                state.StateObject = new WorkflowStateObject();
            }
            var workflowInstance = (state.StateObject.Instance as Definition.WorkflowContainer)
                ?? _hydrator.CreateInstance(workflowTypes.WorkflowContainer);
            state.StateObject.Instance = workflowInstance;

            // Restore cancelled tokens from history
            if (state.CancellationHistory != null && state.CancellationHistory.Count > 0)
            {
                workflowInstance.TokensToCancel = state.CancellationHistory.GetCancelledTokens();
            }

            // Check if this wait belongs to a sub-workflow
            var parentSubWorkflowDto = (triggeringWaitDto != null && triggeringWaitDto.ParentWaitId.HasValue)
                ? FindWaitById(state.Waits, triggeringWaitDto.ParentWaitId.Value) as SubWorkflowWaitDto
                : null;

            IAsyncEnumerable<Definition.Wait> workflowStream;

            if (parentSubWorkflowDto != null)
            {
                // This wait belongs to a sub-workflow - use the parent's CallerName to invoke the workflow method
                // Retrieve child state
                if (!state.StateObject.StateMachinesObjects?.TryGetValue(parentSubWorkflowDto.Id.ToString(), out var storedChildState) == true)
                {
                    throw new InvalidOperationException($"Sub-workflow state not found for SubWorkflowWait '{parentSubWorkflowDto.WaitName}'.");
                }

                var callerName = string.IsNullOrEmpty(parentSubWorkflowDto.CallerName) ? "Run" : parentSubWorkflowDto.CallerName;
                var invoker = _hydrator.GetInvoker(workflowTypes.WorkflowContainer, callerName);
                workflowStream = (IAsyncEnumerable<Definition.Wait>)invoker(workflowInstance);
            }
            else
            {
                var callerName = (triggeringWaitDto != null && !string.IsNullOrEmpty(triggeringWaitDto.CallerName)) ? triggeringWaitDto.CallerName : "Run";
                var invoker = _hydrator.GetInvoker(workflowTypes.WorkflowContainer, callerName);
                workflowStream = (IAsyncEnumerable<Definition.Wait>)invoker(workflowInstance);
            }

            context.Signal = incomingRequest.Signal;
            context.CommandResult = incomingRequest.CommandResult;
            context.WorkflowState = state;
            context.WorkflowInstance = workflowInstance;
            context.TriggeringWaitId = incomingRequest.TriggeringWaitId;
            context.WorkflowStream = workflowStream;
            context.ContinueExecutionLoop = false;

            context.ConsumedWaitsIds.Clear();
            if (triggeringWaitDto != null)
            {
                context.ConsumedWaitsIds.Add(triggeringWaitDto.Id);
            }
        }

        /// <summary>
        /// Maps the execution context back to a result DTO for persistence.
        /// </summary>
        public AsyncResult MapToResultDto(WorkflowExecutionContext context)
        {
            var state = context.WorkflowState;

            // Sync cancelled tokens from workflow instance back to state history
            if (context.WorkflowInstance.TokensToCancel.Count > 0)
            {
                var existingTokens = state.CancellationHistory.GetCancelledTokens();
                foreach (var token in context.WorkflowInstance.TokensToCancel)
                {
                    if (!existingTokens.Contains(token))
                    {
                        state.CancellationHistory.Add(new CancellationHistoryEntry
                        {
                            Token = token,
                            CancelledAt = DateTime.UtcNow,
                            Reason = "Triggered during workflow execution"
                        });
                    }
                }
            }

            // Collect completed/canceled/in-error wait IDs recursively
            var completedIds = new HashSet<Guid>();
            foreach (var wait in state.Waits)
            {
                CollectCompletedWaitsRecursive(wait, completedIds);
            }

            // Add all collected completed IDs to context.ConsumedWaitsIds
            foreach (var id in completedIds)
            {
                context.ConsumedWaitsIds.Add(id);
            }

            // Do not prune completed/canceled/in-error waits from state.Waits tree
            // state.Waits.RemoveAll(w => w.Status == Abstraction.Enums.WaitStatus.Completed ||
            //                           w.Status == Abstraction.Enums.WaitStatus.Canceled ||
            //                           w.Status == Abstraction.Enums.WaitStatus.InError ||
            //                           context.ConsumedWaitsIds.Contains(w.Id));

            return new AsyncResult(
                state.Id,
                new
                {
                    NewWaitsIds = state.Waits.Where(w => w.Status == Abstraction.Enums.WaitStatus.Waiting).Select(w => w.Id).ToList(),
                    context.ConsumedWaitsIds
                },
                "Accepted",
                "Workflow advanced.",
                DateTime.UtcNow);
        }

        private void CollectCompletedWaitsRecursive(WaitInfrastructureDto wait, HashSet<Guid> completedIds)
        {
            if (wait == null) return;

            if (wait.Status == Abstraction.Enums.WaitStatus.Completed ||
                wait.Status == Abstraction.Enums.WaitStatus.Canceled ||
                wait.Status == Abstraction.Enums.WaitStatus.InError ||
                wait.Status == Abstraction.Enums.WaitStatus.Matched)
            {
                CollectAllIdsRecursive(wait, completedIds);
                return;
            }

            if (wait.ChildWaits != null)
            {
                foreach (var child in wait.ChildWaits)
                {
                    CollectCompletedWaitsRecursive(child, completedIds);
                }
            }
        }

        private void CollectAllIdsRecursive(WaitInfrastructureDto wait, HashSet<Guid> ids)
        {
            if (wait == null) return;
            ids.Add(wait.Id);
            if (wait.ChildWaits != null)
            {
                foreach (var child in wait.ChildWaits)
                {
                    CollectAllIdsRecursive(child, ids);
                }
            }
        }

        public WaitInfrastructureDto FindWaitById(IEnumerable<WaitInfrastructureDto> waits, Guid id)
        {
            if (waits == null) return null;

            var stack = new Stack<WaitInfrastructureDto>(waits.Where(w => w != null));
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

        /// <summary>
        /// Gets parent workflow stream for resumption after sub-workflow completion.
        /// </summary>
        public IAsyncEnumerable<Definition.Wait> GetParentWorkflowStream(
            string workflowType,
            Definition.WorkflowContainer workflowInstance,
            string callerName)
        {
            if (!_workflowRegistry.Workflows.TryGetValue(workflowType, out var workflowTypes))
                throw new InvalidOperationException($"Workflow '{workflowType}' not registered.");

            var invoker = _hydrator.GetInvoker(workflowTypes.WorkflowContainer, string.IsNullOrEmpty(callerName) ? "Run" : callerName);
            return (IAsyncEnumerable<Definition.Wait>)invoker(workflowInstance);
        }

        /// <summary>
        /// Populates a fresh execution context for a brand-new workflow instance.
        /// </summary>
        public void PopulateNewWorkflowContext(WorkflowExecutionContext context, string workflowName, object input = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!_workflowRegistry.Workflows.TryGetValue(workflowName, out var workflowTypes))
                throw new InvalidOperationException($"Workflow '{workflowName}' not registered.");

            // Instantiate the workflow container
            var workflowInstance = _hydrator.CreateInstance(workflowTypes.WorkflowContainer);

            // Copy public properties of the input object to the instantiated workflow container
            if (input != null)
            {
                var inputType = input.GetType();
                var containerType = workflowInstance.GetType();
                foreach (var inputProp in inputType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    if (!inputProp.CanRead) continue;
                    var containerProp = containerType.GetProperty(inputProp.Name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (containerProp != null && containerProp.CanWrite)
                    {
                        var value = inputProp.GetValue(input);
                        containerProp.SetValue(workflowInstance, value);
                    }
                }
            }

            // Get the top-level Run stream
            var invoker = _hydrator.GetInvoker(workflowTypes.WorkflowContainer, nameof(Definition.WorkflowContainer.Run));
            var workflowStream = (IAsyncEnumerable<Definition.Wait>)invoker(workflowInstance);

            var freshState = new WorkflowStateDto
            {
                Id = Guid.NewGuid(),
                Created = DateTime.UtcNow,
                WorkflowType = workflowName,
                Status = Abstraction.Enums.WorkflowInstanceStatus.New,
                StateObject = new WorkflowStateObject
                {
                    Instance = workflowInstance,
                    StateIndex = -1,
                    StateMachinesObjects = new Dictionary<string, object>(),
                    WaitStatesObjects = new Dictionary<Guid, object>()
                },
                Waits = new List<WaitInfrastructureDto>(),
                CancellationHistory = new List<CancellationHistoryEntry>()
            };

            context.WorkflowState = freshState;
            context.WorkflowInstance = workflowInstance;
            context.WorkflowStream = workflowStream;
            context.ContinueExecutionLoop = false;
            context.ConsumedWaitsIds.Clear();
        }
    }
}
