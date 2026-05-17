using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using FastExpressionCompiler;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Runner;

namespace Workflows.Runner.Pipeline
{
    /// <summary>
    /// Service for managing workflow state and creating execution contexts.
    /// </summary>
    internal class WorkflowStateService
    {
        private readonly IWorkflowRegistry _workflowRegistry;
        private readonly IServiceProvider _serviceProvider;
        private readonly Mapper _mapper;

        // Cache workflow factories and invokers
        private static readonly ConcurrentDictionary<Type, ObjectFactory> _workflowFactories = new();
        private static readonly ConcurrentDictionary<string, Func<object, object>> _workflowInvokers = new();

        public WorkflowStateService(
            IWorkflowRegistry workflowRegistry,
            IServiceProvider serviceProvider,
            Mapper mapper)
        {
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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
            var workflowFactory = GetOrAddWorkflowFactory(workflowTypes.WorkflowContainer);
            var workflowInstance = (Definition.WorkflowContainer)workflowFactory(_serviceProvider, null);

            // Restore cancelled tokens from history
            if (state.CancellationHistory != null && state.CancellationHistory.Count > 0)
            {
                workflowInstance.TokensToCancel = state.CancellationHistory.GetCancelledTokens();
            }

            // Map triggering wait
            var triggeringWait = _mapper.MapToWait(triggeringWaitDto, _workflowRegistry, state.StateObject);
            triggeringWait.WorkflowContainer = workflowInstance;

            // Check if this wait belongs to a sub-workflow
            var parentSubWorkflowDto = triggeringWaitDto.ParentWaitId.HasValue
                ? FindWaitById(state.Waits, triggeringWaitDto.ParentWaitId.Value) as SubWorkflowWaitDto
                : null;

            Definition.SubWorkflowWait parentSubWorkflow = null;
            WorkflowStateObject subWorkflowState = null;
            IAsyncEnumerable<Definition.Wait> workflowStream;
            WorkflowStateObject activeState;

            if (parentSubWorkflowDto != null)
            {
                // This wait belongs to a sub-workflow
                parentSubWorkflow = _mapper.MapToWait(parentSubWorkflowDto, _workflowRegistry, state.StateObject) as Definition.SubWorkflowWait;
                parentSubWorkflow.WorkflowContainer = workflowInstance;

                // Retrieve child state
                if (state.StateObject.StateMachinesObjects?.TryGetValue(parentSubWorkflow.Id, out var storedChildState) == true)
                {
                    subWorkflowState = storedChildState as WorkflowStateObject;
                }

                if (subWorkflowState == null)
                {
                    throw new InvalidOperationException($"Sub-workflow state not found for SubWorkflowWait '{parentSubWorkflow.WaitName}'.");
                }

                workflowStream = parentSubWorkflow.Runner;
                activeState = subWorkflowState;
            }
            else
            {
                // Resume parent workflow
                var workflowInvoker = GetOrAddWorkflowInvoker(workflowTypes.WorkflowContainer, triggeringWait.CallerName);
                workflowStream = (IAsyncEnumerable<Definition.Wait>)workflowInvoker(workflowInstance);
                activeState = state.StateObject;
            }

            var context = new WorkflowExecutionContext
            {
                Signal = incomingRequest.Signal,
                CommandResult = incomingRequest.CommandResult,
                WorkflowState = state,
                WorkflowInstance = workflowInstance,
                TriggeringWaitId = incomingRequest.TriggeringWaitId,
                TriggeringWaitDto = triggeringWaitDto,
                TriggeringWait = triggeringWait,
                ParentSubWorkflow = parentSubWorkflow,
                ActiveState = activeState,
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

            // Update state status if completed
            if (context.IsWorkflowCompleted)
            {
                state.Status = Abstraction.Enums.WorkflowInstanceStatus.Completed;
            }

            // Update waits
            state.Waits = context.NewWaits;

            return new AsyncResult(
                Guid.NewGuid(),
                new
                {
                    NewWaitsIds = context.NewWaits.Select(w => w.Id).ToList(),
                    ConsumedWaitsIds = context.ConsumedWaitsIds
                },
                "Accepted",
                "Workflow advanced.",
                DateTime.UtcNow);
        }

        private static WaitInfrastructureDto FindWaitById(IEnumerable<WaitInfrastructureDto> waits, Guid id)
        {
            if (waits == null) return null;

            foreach (var wait in waits)
            {
                if (wait == null) continue;
                if (wait.Id == id) return wait;

                var child = FindWaitById(wait.ChildWaits, id);
                if (child != null) return child;
            }

            return null;
        }

        private static ObjectFactory GetOrAddWorkflowFactory(Type workflowType)
        {
            return _workflowFactories.GetOrAdd(workflowType, type => 
                ActivatorUtilities.CreateFactory(type, Array.Empty<Type>()));
        }

        private static Func<object, object> GetOrAddWorkflowInvoker(Type containerType, string methodName)
        {
            var key = $"{containerType.FullName}:{methodName}";
            return _workflowInvokers.GetOrAdd(key, _ =>
            {
                var method = containerType.GetMethod(methodName, 
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null) return null;

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var call = Expression.Call(Expression.Convert(instanceParam, containerType), method);
                var lambda = Expression.Lambda<Func<object, object>>(
                    Expression.Convert(call, typeof(object)), instanceParam);
                return lambda.CompileFast();
            });
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
            {
                throw new InvalidOperationException($"Workflow {workflowType} not registered.");
            }

            var workflowInvoker = GetOrAddWorkflowInvoker(workflowTypes.WorkflowContainer, callerName);
            if (workflowInvoker == null)
            {
                throw new InvalidOperationException($"Workflow method {callerName} not found.");
            }

            return (IAsyncEnumerable<Definition.Wait>)workflowInvoker(workflowInstance);
        }
    }
}
