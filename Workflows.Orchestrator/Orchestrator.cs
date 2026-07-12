using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;
using Workflows.Primitives;

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
        private readonly ITemplateRepository? _templateRepository;
        private readonly IOutboxNotificationDispatcher? _outboxNotificationDispatcher;
        private readonly IInstanceLockManager? _instanceLockManager;

        /// <summary>
        /// Unique identifier for this orchestrator node. Used as the owner of instance locks.
        /// </summary>
        private readonly string _nodeId = Environment.MachineName + "-" + Guid.NewGuid().ToString("N");

        /// <summary>
        /// Default TTL for instance locks. Prevents permanent lockout if a node crashes mid-processing.
        /// </summary>
        private static readonly TimeSpan DefaultLockTtl = TimeSpan.FromMinutes(5);

        public Orchestrator(
            IWorkflowStore workflowStore,
            IDefinitionRepository definitionRepository,
            IWorkflowRunner runner,
            IObjectSerializer serializer,
            IWorkflowRegistry workflowRegistry,
            IWorkflowCloner workflowCloner,
            ISignalPreFilter signalPreFilter,
            ITemplateRepository? templateRepository = null,
            IOutboxNotificationDispatcher? outboxNotificationDispatcher = null,
            IInstanceLockManager? instanceLockManager = null)
        {
            _workflowStore = workflowStore ?? throw new ArgumentNullException(nameof(workflowStore));
            _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
            _workflowCloner = workflowCloner ?? throw new ArgumentNullException(nameof(workflowCloner));
            _signalPreFilter = signalPreFilter ?? throw new ArgumentNullException(nameof(signalPreFilter));
            _templateRepository = templateRepository;
            _outboxNotificationDispatcher = outboxNotificationDispatcher;
            _instanceLockManager = instanceLockManager;
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

            int maxAttempts = 5;
            int baseDelayMs = 50;
            Random rand = new Random();
            var runState = state;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var request = new WorkflowExecutionRequest
                    {
                        TriggeringWaitId = commandResultDto.CommandWaitId,
                        WorkflowState = runState,
                        CommandResult = commandResultDto.Result
                    };

                    await _runner.RunWorkflowAsync(request);
                    break;
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (attempt == maxAttempts)
                    {
                        throw;
                    }

                    int delay = baseDelayMs * (int)Math.Pow(2, attempt - 1) + rand.Next(0, baseDelayMs);
                    await Task.Delay(delay);

                    // Re-fetch state
                    var freshState = await _workflowStore.GetInstanceStateAsync(instanceId);
                    if (freshState == null)
                    {
                        break;
                    }

                    // Verify the command wait is still active (Waiting status)
                    var freshCommandWait = WaitFinder.FindWaitingRecordForCommand(freshState.Waits, commandResultDto.CommandWaitId);
                    if (freshCommandWait == null)
                    {
                        break; // Already completed/progressed
                    }

                    runState = freshState;
                }
            }
        }

        public async Task ProcessSignalAsync(SignalDto signalDto)
        {
            if (signalDto == null) throw new ArgumentNullException(nameof(signalDto));
            if (string.IsNullOrEmpty(signalDto.SignalIdentifier)) throw new ArgumentException("SignalIdentifier must be provided.", nameof(signalDto));

            if (signalDto.Id == Guid.Empty)
            {
                signalDto.Id = Guid.NewGuid();
            }

            if (await _workflowStore.HasSignalBeenProcessedAsync(signalDto.Id))
            {
                Console.WriteLine($"[IDEMPOTENCE] Signal {signalDto.Id} has already been processed. Discarding duplicate.");
                return;
            }


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

            // Group candidate states by WorkflowType to evaluate them sequentially per workflow type
            var statesByWorkflowType = new Dictionary<string, List<WorkflowStateDto>>();
            foreach (var instanceId in instanceIds)
            {
                var state = await _workflowStore.GetInstanceStateAsync(instanceId);
                if (state == null) continue;

                var wfType = state.WorkflowType ?? string.Empty;
                if (!statesByWorkflowType.TryGetValue(wfType, out var list))
                {
                    list = new List<WorkflowStateDto>();
                    statesByWorkflowType[wfType] = list;
                }
                list.Add(state);
            }

            foreach (var group in statesByWorkflowType)
            {
                // Order candidate states so that those waiting on a first wait (templates/prototypes)
                // are evaluated LAST, allowing existing active instances of the same workflow type
                // to match and consume the signal first.
                var sortedStates = group.Value.OrderBy(state =>
                {
                    var triggeringWait = WaitFinder.FindWaitingRecordForSignal(state.Waits, signalDto.SignalIdentifier);
                    return (triggeringWait is SignalWaitDto sw && sw.IsFirstWait) ? 1 : 0;
                }).ToList();

                foreach (var state in sortedStates)
                {
                    var triggeringWait = WaitFinder.FindWaitingRecordForSignal(state.Waits, signalDto.SignalIdentifier);
                    if (triggeringWait == null) continue;

                    if (triggeringWait is SignalWaitDto signalWait && !_signalPreFilter.IsMatch(signalWait, signalDto, state))
                    {
                        continue; // Tier 1.5 filter: Skip invoking runner
                    }

                    var isFirstWait = triggeringWait is SignalWaitDto sw && sw.IsFirstWait;

                    WorkflowStateDto runState = state;
                    string triggeringWaitId = triggeringWait.Id;

                    if (isFirstWait)
                    {
                        // Clone the immutable state and update all IDs
                        runState = _workflowCloner.CloneStateWithNewIds(state, out var newTriggeringWaitId, triggeringWait.Id);
                        triggeringWaitId = newTriggeringWaitId;
                    }

                    var triggeringWaitInRunState = WaitFinder.FindWaitById(runState.Waits, triggeringWaitId);
                    if (triggeringWaitInRunState is SignalWaitDto signalWaitInRunState && !string.IsNullOrEmpty(signalWaitInRunState.TemplateHashKey))
                    {
                        var template = _templateRepository?.GetTemplate(signalWaitInRunState.TemplateHashKey);
                        if (template != null && (template.IsGenericMatchFullMatch || template.IsExactMatchFullMatch))
                        {
                            signalWaitInRunState.Status = WaitStatus.Matched;
                            if (!string.IsNullOrEmpty(signalWaitInRunState.ParentWaitId))
                            {
                                var parentWait = WaitFinder.FindWaitById(runState.Waits, signalWaitInRunState.ParentWaitId);
                                if (parentWait is GroupWaitDto parentGroup && parentGroup.WaitType == WaitType.GroupWaitAll)
                                {
                                    bool allCompletedOrMatched = true;
                                    if (parentGroup.ChildWaits != null)
                                    {
                                        foreach (var child in parentGroup.ChildWaits)
                                        {
                                            if (child.Status != WaitStatus.Completed && child.Status != WaitStatus.Matched)
                                            {
                                                allCompletedOrMatched = false;
                                                break;
                                            }
                                        }
                                    }
                                    if (allCompletedOrMatched)
                                    {
                                        parentGroup.Status = WaitStatus.Matched;
                                    }
                                }
                            }
                        }
                    }

                    var request = new WorkflowExecutionRequest
                    {
                        TriggeringWaitId = triggeringWaitId,
                        WorkflowState = runState,
                        Signal = signalDto
                    };

                    int maxAttempts = 5;
                    int baseDelayMs = 50;
                    Random rand = new Random();
                    bool matched = false;

                    for (int attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        try
                        {
                            var runResult = await _runner.RunWorkflowAsync(request);
                            if (runResult != null && runResult.Status != "Unmatched")
                            {
                                matched = true;
                            }
                            break;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[RETRY DEBUG] Attempt {attempt} failed with exception: {ex.GetType().Name}: {ex.Message}");
                            if (attempt == maxAttempts)
                            {
                                throw;
                            }

                            int delay = baseDelayMs * (int)Math.Pow(2, attempt - 1) + rand.Next(0, baseDelayMs);
                            await Task.Delay(delay);

                            // Re-fetch fresh state
                            var freshState = await _workflowStore.GetInstanceStateAsync(state.Id);
                            if (freshState == null)
                            {
                                Console.WriteLine("[RETRY DEBUG] freshState is null!");
                                break; // Instance was deleted
                            }

                            // Re-evaluate if it's still waiting on the signal
                            var freshTriggeringWait = WaitFinder.FindWaitingRecordForSignal(freshState.Waits, signalDto.SignalIdentifier);
                            if (freshTriggeringWait == null)
                            {
                                Console.WriteLine($"[RETRY DEBUG] freshTriggeringWait is null! Waits count: {freshState.Waits?.Count}");
                                break; // It already progressed or is no longer waiting for this signal
                            }

                            if (freshTriggeringWait is SignalWaitDto swDto && !_signalPreFilter.IsMatch(swDto, signalDto, freshState))
                            {
                                break; // No longer matches pre-filter
                            }

                            // Rebuild request
                            request = new WorkflowExecutionRequest
                            {
                                TriggeringWaitId = freshTriggeringWait.Id,
                                WorkflowState = freshState,
                                Signal = signalDto
                            };
                        }
                    }

                    if (matched)
                    {
                        break; // Stop evaluating further candidate instances for this workflow type
                    }
                }
            }
        }

        public async Task<Guid> StartWorkflowAsync(string workflowName, int version, object input)
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

        public async Task CancelWorkflowAsync(Guid instanceId, string token, string reason = "")
        {
            var state = await _workflowStore.GetInstanceStateAsync(instanceId);
            if (state == null)
            {
                throw new InvalidOperationException($"Workflow instance '{instanceId}' not found.");
            }

            var existingTokens = state.CancellationHistory.GetCancelledTokens();
            if (!existingTokens.Contains(token))
            {
                state.CancellationHistory.Add(new CancellationHistoryEntry
                {
                    Token = token,
                    CancelledAt = DateTime.UtcNow,
                    Reason = reason
                });

                await _workflowStore.SaveContextSyncAsync(state, Enumerable.Empty<string>());

                if (_outboxNotificationDispatcher != null)
                {
                    _outboxNotificationDispatcher.NotifyCancellationRequested(instanceId, token, reason);
                }
            }
        }
    }
}
