using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;

namespace Workflows.Orchestrator
{
    public class Orchestrator : IOrchestrator
    {
        private readonly IWorkflowStore _workflowStore;
        private readonly IDefinitionRepository _definitionRepository;
        private readonly IWorkflowRunner _runner;
        private readonly IObjectSerializer _serializer;
        private readonly IWorkflowRegistry _workflowRegistry;

        public Orchestrator(
            IWorkflowStore workflowStore,
            IDefinitionRepository definitionRepository,
            IWorkflowRunner runner,
            IObjectSerializer serializer,
            IWorkflowRegistry workflowRegistry)
        {
            _workflowStore = workflowStore ?? throw new ArgumentNullException(nameof(workflowStore));
            _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
        }

        public async Task ProcessCommandResultAsync(CommandResultDto commandResultDto)
        {
            if (commandResultDto == null) throw new ArgumentNullException(nameof(commandResultDto));

            var instanceId = await _workflowStore.GetInstanceByCommandWaitIdAsync(commandResultDto.CommandWaitId);
            if (instanceId == Guid.Empty)
            {
                throw new InvalidOperationException($"No workflow instance found waiting for command ID '{commandResultDto.CommandWaitId}'.");
            }

            var state = await _workflowStore.GetInstanceStateAsync(instanceId);
            if (state == null)
            {
                throw new InvalidOperationException($"Workflow instance '{instanceId}' not found.");
            }

            object rawResult = commandResultDto.Result;
            var commandWait = FindWaitingRecordForCommand(state.Waits, commandResultDto.CommandWaitId);
            if (commandWait != null && !string.IsNullOrEmpty(commandWait.HandlerKey))
            {
                (Type CommandPayloadType, Type CommandResultType) types = default;
                if (_workflowRegistry.CommandTypes.TryGetValue(commandWait.HandlerKey, out var directTypes))
                {
                    types = directTypes;
                }
                else
                {
                    types = _workflowRegistry.CommandTypes.Values
                        .FirstOrDefault(t => t.CommandPayloadType.FullName == commandWait.HandlerKey || 
                                             t.CommandPayloadType.AssemblyQualifiedName == commandWait.HandlerKey);
                }

                if (types != default)
                {
                    var resultType = types.CommandResultType;
                    if (rawResult != null && !resultType.IsAssignableFrom(rawResult.GetType()))
                    {
                        try
                        {
                            if (rawResult is string jsonStr)
                            {
                                var deserialized = _serializer.Deserialize(jsonStr, resultType);
                                if (deserialized != null)
                                {
                                    rawResult = deserialized;
                                }
                            }
                            else
                            {
                                var json = _serializer.Serialize(rawResult);
                                var jsonStr2 = json as string ?? json?.ToString();
                                if (jsonStr2 != null)
                                {
                                    var deserialized = _serializer.Deserialize(jsonStr2, resultType);
                                    if (deserialized != null)
                                    {
                                        rawResult = deserialized;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Keep rawResult as is if deserialization fails
                        }
                    }
                }
            }

            commandResultDto.Result = rawResult;

            var request = new WorkflowExecutionRequest
            {
                TriggeringWaitId = commandResultDto.CommandWaitId,
                WorkflowState = state,
                CommandResult = commandResultDto.Result
            };

            await _runner.RunWorkflowAsync(request);
        }

        public async Task ProcessSignalAsync(SignalDto signalDto)
        {
            if (signalDto == null) throw new ArgumentNullException(nameof(signalDto));
            if (string.IsNullOrEmpty(signalDto.SignalIdentifier)) throw new ArgumentException("SignalIdentifier must be provided.", nameof(signalDto));

            object rawData = signalDto.Data;

            if (rawData is string || rawData is JsonElement || rawData is System.Text.Json.JsonDocument)
            {
                var signalDef = await _definitionRepository.GetSignalDefinitionAsync(signalDto.SignalIdentifier);
                if (signalDef != null && !string.IsNullOrEmpty(signalDef.PayloadTypeName))
                {
                    var payloadType = Type.GetType(signalDef.PayloadTypeName);
                    if (payloadType != null)
                    {
                        var json = rawData is string s ? s : rawData?.ToString();
                        if (json != null)
                        {
                            try
                            {
                                var deserialized = _serializer.Deserialize(json, payloadType);
                                if (deserialized != null)
                                {
                                    rawData = deserialized;
                                }
                            }
                            catch
                            {
                                // Keep rawData as is if deserialization fails
                            }
                        }
                    }
                }
            }

            signalDto.Data = rawData;

            var signalDataJson = signalDto.Data != null ? Newtonsoft.Json.JsonConvert.SerializeObject(signalDto.Data, new Newtonsoft.Json.Converters.StringEnumConverter()) : string.Empty;
            var instanceIds = await _workflowStore.FindInstancesWaitingForSignalAsync(signalDto.SignalIdentifier, signalDataJson);
            foreach (var instanceId in instanceIds)
            {
                var state = await _workflowStore.GetInstanceStateAsync(instanceId);
                if (state == null) continue;

                var triggeringWait = FindWaitingRecordForSignal(state.Waits, signalDto.SignalIdentifier);
                if (triggeringWait == null) continue;

                var isFirstWait = triggeringWait is SignalWaitDto sw && sw.IsFirstWait;

                WorkflowStateDto runState = state;
                Guid triggeringWaitId = triggeringWait.Id;

                if (isFirstWait)
                {
                    // Clone the immutable state and update all IDs
                    runState = CloneStateWithNewIds(state, out var newTriggeringWaitId, triggeringWait.Id);
                    triggeringWaitId = newTriggeringWaitId;
                }

                var request = new WorkflowExecutionRequest
                {
                    TriggeringWaitId = triggeringWaitId,
                    WorkflowState = runState,
                    Signal = signalDto
                };

                await _runner.RunWorkflowAsync(request);
            }
        }

        private WorkflowStateDto CloneStateWithNewIds(WorkflowStateDto source, out Guid newTriggeringWaitId, Guid oldTriggeringWaitId)
        {
            var clone = new WorkflowStateDto
            {
                Id = Guid.NewGuid(),
                Created = DateTime.UtcNow,
                Status = source.Status,
                WorkflowType = source.WorkflowType,
                StateObject = CloneStateObject(source.StateObject),
                Waits = new List<WaitInfrastructureDto>(),
                CancellationHistory = source.CancellationHistory != null ? new List<CancellationHistoryEntry>(source.CancellationHistory) : new()
            };

            // 1. Build a map of old wait ID -> new wait ID
            var idMap = new Dictionary<Guid, Guid>();
            BuildIdMapRecursive(source.Waits, idMap);

            // 2. Map the triggering wait ID
            if (idMap.TryGetValue(oldTriggeringWaitId, out var mappedTriggerId))
            {
                newTriggeringWaitId = mappedTriggerId;
            }
            else
            {
                newTriggeringWaitId = oldTriggeringWaitId;
            }

            // 3. Clone and apply new IDs to the waits
            foreach (var wait in source.Waits)
            {
                clone.Waits.Add(CloneAndRemapWaitRecursive(wait, idMap));
            }

            // 4. Update keys in WaitStatesObjects in StateObject
            if (clone.StateObject?.WaitStatesObjects != null)
            {
                var updatedWaitStates = new Dictionary<Guid, object>();
                foreach (var kvp in clone.StateObject.WaitStatesObjects)
                {
                    if (idMap.TryGetValue(kvp.Key, out var newWaitId))
                    {
                        updatedWaitStates[newWaitId] = kvp.Value;
                    }
                    else
                    {
                        updatedWaitStates[kvp.Key] = kvp.Value;
                    }
                }
                clone.StateObject.WaitStatesObjects = updatedWaitStates;
            }

            return clone;
        }

        private void BuildIdMapRecursive(IEnumerable<WaitInfrastructureDto> waits, Dictionary<Guid, Guid> idMap)
        {
            if (waits == null) return;
            foreach (var w in waits)
            {
                idMap[w.Id] = Guid.NewGuid();
                if (w.ChildWaits != null)
                {
                    BuildIdMapRecursive(w.ChildWaits, idMap);
                }
            }
        }

        private WaitInfrastructureDto CloneAndRemapWaitRecursive(WaitInfrastructureDto wait, Dictionary<Guid, Guid> idMap)
        {
            // Serialize and deserialize to clone the wait DTO polymorphically
            var serialized = _serializer.Serialize(wait, SerializationScope.CompilerGeneratedClass);
            var cloned = (WaitInfrastructureDto)_serializer.Deserialize(serialized, wait.GetType(), SerializationScope.CompilerGeneratedClass);

            // Apply new ID
            if (idMap.TryGetValue(wait.Id, out var newId))
            {
                cloned.Id = newId;
            }
            else
            {
                cloned.Id = Guid.NewGuid();
            }

            cloned.IsPersisted = false; // Reset persistence flag so it gets indexed

            // Reset parent ID
            if (wait.ParentWaitId.HasValue && idMap.TryGetValue(wait.ParentWaitId.Value, out var newParentId))
            {
                cloned.ParentWaitId = newParentId;
            }

            // Clone and apply to children recursively
            if (wait.ChildWaits != null)
            {
                cloned.ChildWaits = new List<WaitInfrastructureDto>();
                foreach (var child in wait.ChildWaits)
                {
                    cloned.ChildWaits.Add(CloneAndRemapWaitRecursive(child, idMap));
                }
            }

            return cloned;
        }

        private WorkflowStateObject CloneStateObject(WorkflowStateObject source)
        {
            if (source == null) return null;
            var serialized = _serializer.Serialize(source, SerializationScope.CompilerGeneratedClass);
            return _serializer.Deserialize<WorkflowStateObject>(serialized, SerializationScope.CompilerGeneratedClass);
        }

        public async Task<Guid> StartWorkflowAsync(string workflowName, string version, object input)
        {
            if (string.IsNullOrEmpty(workflowName)) throw new ArgumentNullException(nameof(workflowName));

            // Validate that definition exists
            var definition = await _definitionRepository.GetDefinitionAsync(workflowName, version);
            if (definition == null)
            {
                throw new InvalidOperationException($"Workflow '{workflowName}' with version '{version}' is not registered.");
            }

            var result = await _runner.StartWorkflow(workflowName, input);
            return result.Id;
        }

        private WaitInfrastructureDto FindWaitingRecordForSignal(IEnumerable<WaitInfrastructureDto> waits, string signalPath)
        {
            if (waits == null) return null;

            foreach (var w in waits)
            {
                if (w is SignalWaitDto signalWait && signalWait.SignalIdentifier == signalPath && signalWait.Status == WaitStatus.Waiting)
                {
                    return signalWait;
                }
                if (w is TimeWaitDto timeWait && timeWait.UniqueMatchId == signalPath && timeWait.Status == WaitStatus.Waiting)
                {
                    return timeWait;
                }

                if (w.ChildWaits != null && w.ChildWaits.Count > 0)
                {
                    var child = FindWaitingRecordForSignal(w.ChildWaits, signalPath);
                    if (child != null) return child;
                }
            }

            return null;
        }

        private CommandWaitDto FindWaitingRecordForCommand(IEnumerable<WaitInfrastructureDto> waits, Guid commandWaitId)
        {
            if (waits == null) return null;

            foreach (var w in waits)
            {
                if (w is CommandWaitDto commandWait && commandWait.Id == commandWaitId)
                {
                    return commandWait;
                }

                if (w.ChildWaits != null && w.ChildWaits.Count > 0)
                {
                    var child = FindWaitingRecordForCommand(w.ChildWaits, commandWaitId);
                    if (child != null) return child;
                }
            }

            return null;
        }
    }
}
