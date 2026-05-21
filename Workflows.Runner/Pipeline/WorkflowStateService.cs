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
        /// Creates a clean execution context from the incoming request.
        /// </summary>
        public WorkflowExecutionContext CreateExecutionContext(WorkflowExecutionRequest incomingRequest)
        {
            if (incomingRequest == null) throw new ArgumentNullException(nameof(incomingRequest));
            if (incomingRequest.WorkflowState == null) throw new ArgumentException("WorkflowState is required.", nameof(incomingRequest));
            if (incomingRequest.WorkflowState.Waits == null) throw new ArgumentException("WorkflowState.Waits is required.", nameof(incomingRequest));

            var state = incomingRequest.WorkflowState;

            // Find the triggering wait
            var triggeringWaitDto = FindWaitById(state.Waits, incomingRequest.TriggeringWaitId);
            if (triggeringWaitDto == null)
            {
                throw new InvalidOperationException($"Triggering wait with ID {incomingRequest.TriggeringWaitId} not found.");
            }

            if (triggeringWaitDto.Status != Abstraction.Enums.WaitStatus.Waiting)
            {
                throw new InvalidOperationException("Triggering wait is not in Waiting status.");
            }

            // Get workflow types
            if (!_workflowRegistry.Workflows.TryGetValue(state.WorkflowType, out var workflowTypes))
            {
                throw new InvalidOperationException($"Workflow {state.WorkflowType} not registered.");
            }

            // Create workflow instance
            var workflowInstance = _hydrator.CreateInstance(workflowTypes.WorkflowContainer);

            // Restore cancelled tokens from history
            if (state.CancellationHistory != null && state.CancellationHistory.Count > 0)
            {
                workflowInstance.TokensToCancel = state.CancellationHistory.GetCancelledTokens();
            }

            // Check if this wait belongs to a sub-workflow
            var parentSubWorkflowDto = triggeringWaitDto.ParentWaitId.HasValue
                ? FindWaitById(state.Waits, triggeringWaitDto.ParentWaitId.Value) as SubWorkflowWaitDto
                : null;

            IAsyncEnumerable<Definition.Wait> workflowStream;

            if (parentSubWorkflowDto != null)
            {
                // This wait belongs to a sub-workflow - use the parent's CallerName to invoke the workflow method
                // Retrieve child state
                if (!state.StateObject.StateMachinesObjects?.TryGetValue(parentSubWorkflowDto.Id, out var storedChildState) == true)
                {
                    throw new InvalidOperationException($"Sub-workflow state not found for SubWorkflowWait '{parentSubWorkflowDto.WaitName}'.");
                }

                var invoker = _hydrator.GetInvoker(workflowTypes.WorkflowContainer, parentSubWorkflowDto.CallerName);
                workflowStream = (IAsyncEnumerable<Definition.Wait>)invoker(workflowInstance);
            }
            else
            {
                var invoker = _hydrator.GetInvoker(workflowTypes.WorkflowContainer, triggeringWaitDto.CallerName);
                workflowStream = (IAsyncEnumerable<Definition.Wait>)invoker(workflowInstance);
            }

            var context = new WorkflowExecutionContext
            {
                Signal = incomingRequest.Signal,
                CommandResult = incomingRequest.CommandResult,
                WorkflowState = state,
                WorkflowInstance = workflowInstance,
                TriggeringWaitId = incomingRequest.TriggeringWaitId,
                WorkflowStream = workflowStream,
                ContinueExecutionLoop = false
            };

            context.ConsumedWaitsIds.Add(triggeringWaitDto.Id);

            return context;
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

            // Note: State.Status and State.Waits are already updated by processors and matchers directly
            // No need to copy from context since we're using WorkflowState.Waits and WorkflowState.Status directly

            return new AsyncResult(
                Guid.NewGuid(),
                new
                {
                    NewWaitsIds = state.Waits.Select(w => w.Id).ToList(),
                    ConsumedWaitsIds = context.ConsumedWaitsIds
                },
                "Accepted",
                "Workflow advanced.",
                DateTime.UtcNow);
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

            var invoker = _hydrator.GetInvoker(workflowTypes.WorkflowContainer, callerName);
            return (IAsyncEnumerable<Definition.Wait>)invoker(workflowInstance);
        }

        /// <summary>
        /// Creates a fresh execution context for a brand-new workflow instance.
        /// </summary>
        public WorkflowExecutionContext CreateNewWorkflowContext(string workflowName)
        {
            if (!_workflowRegistry.Workflows.TryGetValue(workflowName, out var workflowTypes))
                throw new InvalidOperationException($"Workflow '{workflowName}' not registered.");

            // Instantiate the workflow container
            var workflowInstance = _hydrator.CreateInstance(workflowTypes.WorkflowContainer);

            // Get the top-level Run stream
            var invoker = _hydrator.GetInvoker(workflowTypes.WorkflowContainer, nameof(Definition.WorkflowContainer.Run));
            var workflowStream = (IAsyncEnumerable<Definition.Wait>)invoker(workflowInstance);

            var freshState = new WorkflowStateDto
            {
                Id = Guid.NewGuid(),
                Created = DateTime.UtcNow,
                WorkflowType = workflowName,
                Status = Abstraction.Enums.WorkflowInstanceStatus.New,
                StateObject = new WorkflowStateObject(),
                Waits = new List<WaitInfrastructureDto>(),
                CancellationHistory = new List<CancellationHistoryEntry>()
            };

            return new WorkflowExecutionContext
            {
                WorkflowState = freshState,
                WorkflowInstance = workflowInstance,
                WorkflowStream = workflowStream,
                ContinueExecutionLoop = false
            };
        }
    }
}
