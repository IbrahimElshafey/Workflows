using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly IWorkflowCloner _workflowCloner;
        private readonly ISignalPreFilter _signalPreFilter;

        public Orchestrator(
            IWorkflowStore workflowStore,
            IDefinitionRepository definitionRepository,
            IWorkflowRunner runner,
            IObjectSerializer serializer,
            IWorkflowRegistry workflowRegistry,
            IWorkflowCloner workflowCloner,
            ISignalPreFilter signalPreFilter)
        {
            _workflowStore = workflowStore ?? throw new ArgumentNullException(nameof(workflowStore));
            _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
            _workflowCloner = workflowCloner ?? throw new ArgumentNullException(nameof(workflowCloner));
            _signalPreFilter = signalPreFilter ?? throw new ArgumentNullException(nameof(signalPreFilter));
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
            var commandWait = WaitFinder.FindWaitingRecordForCommand(state.Waits, commandResultDto.CommandWaitId);
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

                var triggeringWait = WaitFinder.FindWaitingRecordForSignal(state.Waits, signalDto.SignalIdentifier);
                if (triggeringWait == null) continue;

                if (triggeringWait is SignalWaitDto signalWait && !_signalPreFilter.IsMatch(signalWait, signalDto, state))
                {
                    continue; // Tier 1.5 filter: Skip invoking runner
                }

                var isFirstWait = triggeringWait is SignalWaitDto sw && sw.IsFirstWait;

                WorkflowStateDto runState = state;
                Guid triggeringWaitId = triggeringWait.Id;

                if (isFirstWait)
                {
                    // Clone the immutable state and update all IDs
                    runState = _workflowCloner.CloneStateWithNewIds(state, out var newTriggeringWaitId, triggeringWait.Id);
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
    }
}
