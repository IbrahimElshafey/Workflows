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
            if (!string.IsNullOrEmpty(incomingRequest.TriggeringWaitId))
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

            // Get workflow types — resolve by instance version first, fall back to latest
            if (!_workflowRegistry.TryGetWorkflow(state.WorkflowType, state.WorkflowVersion, out var workflowTypes))
            {
                if (!_workflowRegistry.TryGetLatestWorkflow(state.WorkflowType, out workflowTypes))
                {
                    throw new InvalidOperationException($"Workflow {state.WorkflowType} (V{state.WorkflowVersion}) not registered.");
                }
            }

            // Create or reuse workflow instance
            if (state.StateObject == null)
            {
                state.StateObject = new WorkflowStateObject { WorkflowType = state.WorkflowType };
            }
            else
            {
                state.StateObject.WorkflowType = state.WorkflowType;
            }
            var workflowInstance = (state.StateObject.Instance as Definition.WorkflowContainer)
                ?? _hydrator.CreateInstance(workflowTypes.WorkflowContainer);
            state.StateObject.Instance = workflowInstance;

            var stateType = workflowTypes.StateType;
            if (!state.StateObject.Locals.TryGetValue("state", out var stateObj) || stateObj == null)
            {
                if (stateType != typeof(object))
                {
                    stateObj = Activator.CreateInstance(stateType);
                    state.StateObject.Locals["state"] = stateObj;
                }
            }

            // Restore cancelled tokens from history
            if (state.CancellationHistory != null && state.CancellationHistory.Count > 0)
            {
                workflowInstance.TokensToCancel = state.CancellationHistory.GetCancelledTokens();
            }

            // Check if this wait belongs to a sub-workflow
            var parentSubWorkflowDto = (triggeringWaitDto != null && !string.IsNullOrEmpty(triggeringWaitDto.ParentWaitId))
                ? FindWaitById(state.Waits, triggeringWaitDto.ParentWaitId) as SubWorkflowWaitDto
                : null;

            IAsyncEnumerable<Definition.Wait> workflowStream;

            if (parentSubWorkflowDto != null)
            {
                // This wait belongs to a sub-workflow - use the parent's CallerName to invoke the workflow method
                // Retrieve child state
                if (!state.StateObject.Locals.TryGetValue(parentSubWorkflowDto.StateMachineObjectId.ToString(), out var storedChildStateObj)
                    || storedChildStateObj is not WorkflowStateObject childState)
                {
                    throw new InvalidOperationException($"Sub-workflow state not found for SubWorkflowWait '{parentSubWorkflowDto.WaitName}'.");
                }

                var callerName = string.IsNullOrEmpty(parentSubWorkflowDto.CallerName) ? "Run" : parentSubWorkflowDto.CallerName;
                
                // Get the sub-workflow state type from parameter
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

                var invoker = _hydrator.GetInvoker(workflowTypes.WorkflowContainer, callerName, subStateType);
                workflowStream = (IAsyncEnumerable<Definition.Wait>)invoker(workflowInstance, childStateObj);
            }
            else
            {
                var callerName = (triggeringWaitDto != null && !string.IsNullOrEmpty(triggeringWaitDto.CallerName)) ? triggeringWaitDto.CallerName : workflowTypes.StartMethod;
                var invoker = _hydrator.GetInvoker(workflowTypes.WorkflowContainer, callerName, stateType);
                state.StateObject.Locals.TryGetValue("state", out var st);
                workflowStream = (IAsyncEnumerable<Definition.Wait>)invoker(workflowInstance, st);
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

            // Assign wait paths recursively
            foreach (var wait in state.Waits)
            {
                AssignIdsRecursive(wait, null, state);
            }

            // Collect completed/canceled/in-error wait IDs recursively
            var completedIds = new HashSet<string>();
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

        private void AssignIdsRecursive(WaitInfrastructureDto dto, string? parentId, WorkflowStateDto state)
        {
            if (string.IsNullOrEmpty(dto.Id) || !dto.Id.Contains('/'))
            {
                state.WaitCounter++;
                var localId = state.WaitCounter.ToString();
                dto.Id = parentId != null ? $"{localId}/{parentId}" : $"{localId}/{state.Id}";
            }
            dto.ParentWaitId = parentId;

            if (dto.ChildWaits != null)
            {
                foreach (var child in dto.ChildWaits)
                {
                    AssignIdsRecursive(child, dto.Id, state);
                }
            }

            if (dto is ExternalGroupWaitDto externalGroup && externalGroup.ExternalChildWaits != null)
            {
                foreach (var child in externalGroup.ExternalChildWaits)
                {
                    AssignIdsRecursive(child, dto.Id, state);
                }
            }
        }

        private void CollectCompletedWaitsRecursive(WaitInfrastructureDto wait, HashSet<string> completedIds)
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

            if (wait is ExternalGroupWaitDto externalGroup && externalGroup.ExternalChildWaits != null)
            {
                foreach (var child in externalGroup.ExternalChildWaits)
                {
                    CollectCompletedWaitsRecursive(child, completedIds);
                }
            }
        }

        private void CollectAllIdsRecursive(WaitInfrastructureDto wait, HashSet<string> ids)
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

            if (wait is ExternalGroupWaitDto externalGroup && externalGroup.ExternalChildWaits != null)
            {
                foreach (var child in externalGroup.ExternalChildWaits)
                {
                    CollectAllIdsRecursive(child, ids);
                }
            }
        }

        public WaitInfrastructureDto FindWaitById(IEnumerable<WaitInfrastructureDto> waits, string id)
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

                if (current is ExternalGroupWaitDto externalGroup && externalGroup.ExternalChildWaits != null)
                {
                    foreach (var child in externalGroup.ExternalChildWaits)
                    {
                        if (child != null) stack.Push(child);
                    }
                }
            }

            return null;
        }

        public IAsyncEnumerable<Definition.Wait> GetParentWorkflowStream(
            string workflowType,
            Definition.WorkflowContainer workflowInstance,
            string callerName)
        {
            if (!_workflowRegistry.Workflows.TryGetValue(workflowType, out var workflowTypes))
                throw new InvalidOperationException($"Workflow '{workflowType}' not registered.");

            var actualCallerName = string.IsNullOrEmpty(callerName) ? workflowTypes.StartMethod : callerName;

            Type stateType = typeof(object);
            if (actualCallerName == workflowTypes.StartMethod)
            {
                stateType = workflowTypes.StateType;
            }
            else
            {
                var methodInfo = workflowTypes.WorkflowContainer.GetMethod(
                    actualCallerName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (methodInfo != null && methodInfo.GetParameters().Length == 1)
                {
                    stateType = methodInfo.GetParameters()[0].ParameterType;
                }
            }

            object? stateObj = null;
            if (stateType != typeof(object))
            {
                stateObj = Activator.CreateInstance(stateType);
            }

            var invoker = _hydrator.GetInvoker(workflowTypes.WorkflowContainer, actualCallerName, stateType);
            return (IAsyncEnumerable<Definition.Wait>)invoker(workflowInstance, stateObj);
        }

        /// <summary>
        /// Populates a fresh execution context for a brand-new workflow instance.
        /// </summary>
        public void PopulateNewWorkflowContext(WorkflowExecutionContext context, string workflowName, int version, object input = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Resolve by requested version, fall back to latest
            if (!_workflowRegistry.TryGetWorkflow(workflowName, version, out var workflowTypes))
            {
                if (!_workflowRegistry.TryGetLatestWorkflow(workflowName, out workflowTypes))
                {
                    throw new InvalidOperationException($"Workflow '{workflowName}' not registered.");
                }
            }

            // Instantiate the workflow container
            var workflowInstance = _hydrator.CreateInstance(workflowTypes.WorkflowContainer);

            var stateType = workflowTypes.StateType;
            object? stateObj = null;
            if (stateType != typeof(object))
            {
                stateObj = Activator.CreateInstance(stateType);
            }

            // Copy public properties of the input object to the instantiated workflow container and/or state POCO
            if (input != null)
            {
                if (input is Newtonsoft.Json.Linq.JObject jObj)
                {
                    foreach (var property in jObj.Properties())
                    {
                        var containerProp = workflowInstance.GetType().GetProperty(property.Name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                        if (containerProp != null && containerProp.CanWrite)
                        {
                            var val = property.Value.ToObject(containerProp.PropertyType);
                            containerProp.SetValue(workflowInstance, val);
                        }

                        if (stateObj != null)
                        {
                            var stateProp = stateObj.GetType().GetProperty(property.Name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                            if (stateProp != null && stateProp.CanWrite)
                            {
                                var val = property.Value.ToObject(stateProp.PropertyType);
                                stateProp.SetValue(stateObj, val);
                            }
                        }
                    }
                }
                else
                {
                    var inputType = input.GetType();
                    var containerType = workflowInstance.GetType();
                    foreach (var inputProp in inputType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        if (!inputProp.CanRead) continue;
                        var containerProp = containerType.GetProperty(inputProp.Name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        var value = inputProp.GetValue(input);
                        Console.WriteLine($"[PopulateNewWorkflowContext] Input Property = {inputProp.Name}, containerProp found = {containerProp != null}, Value = {value}");
                        if (containerProp != null && containerProp.CanWrite)
                        {
                            containerProp.SetValue(workflowInstance, value);
                        }
                    }

                    if (stateObj != null)
                    {
                        var statePocoType = stateObj.GetType();
                        foreach (var inputProp in inputType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                        {
                            if (!inputProp.CanRead) continue;
                            var stateProp = statePocoType.GetProperty(inputProp.Name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            if (stateProp != null && stateProp.CanWrite)
                            {
                                var value = inputProp.GetValue(input);
                                stateProp.SetValue(stateObj, value);
                            }
                        }
                    }
                }
            }

            // Get the top-level start point stream
            var startMethod = workflowTypes.StartMethod;
            var invoker = _hydrator.GetInvoker(workflowTypes.WorkflowContainer, startMethod, stateType);
            var workflowStream = (IAsyncEnumerable<Definition.Wait>)invoker(workflowInstance, stateObj);

            var resolvedVersion = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<Definition.WorkflowAttribute>(workflowTypes.WorkflowContainer)?.Version ?? 1;

            var freshState = new WorkflowStateDto
            {
                Id = Guid.NewGuid(),
                Created = DateTime.UtcNow,
                WorkflowType = workflowName,
                WorkflowVersion = resolvedVersion,
                Status = Abstraction.Enums.WorkflowInstanceStatus.New,
                StateObject = new WorkflowStateObject
                {
                    WorkflowType = workflowName,
                    Instance = workflowInstance,
                    StateIndex = -1,
                    Locals = new Dictionary<string, object>()
                },
                Waits = new List<WaitInfrastructureDto>(),
                CancellationHistory = new List<CancellationHistoryEntry>()
            };

            if (stateObj != null)
            {
                freshState.StateObject.Locals["state"] = stateObj;
            }

            context.WorkflowState = freshState;
            context.WorkflowInstance = workflowInstance;
            context.WorkflowStream = workflowStream;
            context.ContinueExecutionLoop = false;
            context.ConsumedWaitsIds.Clear();
        }
    }
}
