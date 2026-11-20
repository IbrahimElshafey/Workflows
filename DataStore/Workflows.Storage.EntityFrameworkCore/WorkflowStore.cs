using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Persistence;
using Workflows.Primitives;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class WorkflowStore : IWorkflowStore
    {
        public static int MockConcurrencyRetryCount { get; set; } = 0;

        private readonly WorkflowsDbContext _dbContext;
        private readonly IObjectSerializer _serializer;
        private readonly ITemplateRepository? _templateRepository;
        private readonly IWorkflowInstanceCache? _cache;
        private readonly IOutboxNotificationDispatcher? _outboxNotificationDispatcher;

        public WorkflowStore(
            WorkflowsDbContext dbContext,
            IObjectSerializer serializer,
            ITemplateRepository? templateRepository = null,
            IWorkflowInstanceCache? cache = null,
            IOutboxNotificationDispatcher? outboxNotificationDispatcher = null)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _templateRepository = templateRepository;
            _cache = cache;
            _outboxNotificationDispatcher = outboxNotificationDispatcher;
        }

        public async Task SaveContextSyncAsync(
            WorkflowStateDto state,
            IEnumerable<string> completedWaitIds,
            Guid? triggeringSignalId = null,
            DlqMessage? dlqMessage = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var newWaitsList = new List<WaitInfrastructureDto>();
            CollectNewWaitsRecursive(state.Waits, newWaitsList);

            // Mark all waits in state.Waits as persisted so the serialized JSON reflects this
            MarkWaitsAsPersistedRecursive(state.Waits);

            using (var transaction = await _dbContext.Database.BeginTransactionAsync())
            {
                try
                {
                    if (triggeringSignalId.HasValue)
                    {
                        var msgIdStr = triggeringSignalId.Value.ToString();
                        var alreadyProcessed = await _dbContext.SignalInbox.AnyAsync(x => x.MessageId == msgIdStr);
                        if (alreadyProcessed)
                        {
                            throw new InvalidOperationException($"Duplicate signal message detected during save: {triggeringSignalId.Value}");
                        }

                        _dbContext.SignalInbox.Add(new SignalInboxEntity
                        {
                            MessageId = msgIdStr,
                            WorkflowInstanceId = state.Id,
                            ProcessedAt = DateTime.UtcNow,
                            Created = DateTime.UtcNow
                        });
                    }

                    // 1. Update/Create WorkflowInstance
                    var dbInstance = await _dbContext.WorkflowInstances.FindAsync(state.Id);
                    if (dbInstance == null)
                    {
                        var serializedNewInstance = JsonConvert.SerializeObject(state.StateObject?.Instance, WorkflowsDbContext.PolymorphicSerializerSettings);

                        var candidates = await _dbContext.WorkflowInstances
                            .Where(i => i.WorkflowType == state.WorkflowType && i.Status == (int)WorkflowInstanceStatus.Running)
                            .ToListAsync();

                        // One query per wait table instead of two per candidate (the previous shape was a
                        // textbook N+1: O(candidates) round trips on the hottest save path).
                        var instancesWithFirstWait = new HashSet<Guid>();
                        if (candidates.Count > 0)
                        {
                            var candidateIds = candidates.Select(c => c.Id).ToList();

                            var signalFirstWaits = await _dbContext.SignalWaits
                                .Where(sw => sw.IsFirstWait && candidateIds.Contains(sw.WorkflowInstanceId))
                                .Select(sw => sw.WorkflowInstanceId)
                                .ToListAsync();

                            var externalFirstWaits = await _dbContext.ExternalChildWaits
                                .Where(ec => ec.IsFirstWait && candidateIds.Contains(ec.WorkflowInstanceId))
                                .Select(ec => ec.WorkflowInstanceId)
                                .ToListAsync();

                            foreach (var id in signalFirstWaits) instancesWithFirstWait.Add(id);
                            foreach (var id in externalFirstWaits) instancesWithFirstWait.Add(id);
                        }

                        foreach (var candidate in candidates)
                        {
                            if (instancesWithFirstWait.Contains(candidate.Id))
                            {
                                continue;
                            }
                            var serializedCandidate = JsonConvert.SerializeObject(candidate.StateObject?.Instance, WorkflowsDbContext.PolymorphicSerializerSettings);
                            var waitsEqual = AreWaitsEqual(state.Waits, candidate.Waits);
                            var jsonEqual = serializedCandidate == serializedNewInstance;
                            if (jsonEqual && waitsEqual)
                            {
                                state.Id = candidate.Id;
                                dbInstance = candidate;
                                break;
                            }
                        }
                    }

                    var activeWaitCount = CountActiveWaitsRecursive(state.Waits);
                    var lastAdvanceReason = BuildLastAdvanceReason(state.Waits, triggeringSignalId);
                    var errorMessage = ExtractErrorMessage(state);
                    var completedAt = IsCompletedStatus(state.Status) ? (DateTime?)DateTime.UtcNow : null;

                    if (dbInstance == null)
                    {
                        dbInstance = new WorkflowInstance
                        {
                            Id = state.Id,
                            Created = state.Created,
                            Status = (int)state.Status,
                            WorkflowType = state.WorkflowType,
                            WorkflowVersion = state.WorkflowVersion,
                            StateObject = state.StateObject ?? new(),
                            CancellationHistory = state.CancellationHistory ?? new(),
                            Waits = state.Waits ?? new(),
                            ActiveWaitCount = activeWaitCount,
                            LastAdvanceReason = lastAdvanceReason,
                            ErrorMessage = errorMessage,
                            CompletedAt = completedAt
                        };
                        _dbContext.WorkflowInstances.Add(dbInstance);
                    }
                    else
                    {
                        dbInstance.Status = (int)state.Status;
                        dbInstance.WorkflowVersion = state.WorkflowVersion;
                        dbInstance.StateObject = state.StateObject ?? new();
                        dbInstance.CancellationHistory = state.CancellationHistory ?? new();
                        dbInstance.Waits = state.Waits ?? new();
                        dbInstance.ActiveWaitCount = activeWaitCount;
                        dbInstance.LastAdvanceReason = lastAdvanceReason;
                        dbInstance.ErrorMessage = errorMessage;
                        if (completedAt.HasValue)
                            dbInstance.CompletedAt = completedAt;
                        _dbContext.WorkflowInstances.Update(dbInstance);
                    }

                    // 2. Update status of completed/canceled/in-error waits (check each concrete table — TPC, no base WorkflowWaits)
                    if (completedWaitIds != null)
                    {
                        foreach (var id in completedWaitIds)
                        {
                            var waitDto = FindWaitById(state.Waits, id);
                            var status = waitDto?.Status ?? WaitStatus.Completed;
                            await UpdateWaitStatusAsync(id, status);
                        }
                    }

                    // 3. No longer prune cancelled waits since their statuses are updated via completedWaitIds

                    // 4. Flatten and insert/update new waits into their concrete tables
                    if (newWaitsList.Count > 0)
                    {
                        var flattenedRecords = new List<WorkflowWaitEntity>();
                        var externalChildRecords = new List<ExternalChildWaitEntity>();
                        foreach (var wait in newWaitsList)
                        {
                            FlattenAndCollectWaits(wait, null, state.Id, flattenedRecords, externalChildRecords);
                        }

                        foreach (var record in flattenedRecords)
                        {
                            await UpsertWaitEntityAsync(record);
                        }

                        foreach (var record in externalChildRecords)
                        {
                            await UpsertExternalChildWaitEntityAsync(record);
                        }
                    }

                    var notifications = new List<CommandDispatchNotification>();
                    if (newWaitsList.Count > 0)
                    {
                        foreach (var wait in newWaitsList)
                        {
                            CollectAndInsertOutboxMessagesRecursive(wait, state.Id, notifications);
                        }
                    }

                    // Same-transaction DLQ insert: a terminal CompensationFailed status must never be
                    // committed without its dead letter row, and vice versa.
                    if (dlqMessage != null)
                    {
                        _dbContext.DlqMessages.Add(ToDlqMessageEntity(dlqMessage));
                    }

                    if (state.WorkflowType == "ConcurrencyRetryTest" && MockConcurrencyRetryCount > 0)
                    {
                        MockConcurrencyRetryCount--;
                        throw new DbUpdateConcurrencyException("Mock concurrency conflict");
                    }

                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    if (_cache != null)
                    {
                        if (state.Status == WorkflowInstanceStatus.Completed
                            || state.Status == WorkflowInstanceStatus.InError
                            || state.Status == WorkflowInstanceStatus.CompensationFailed)
                        {
                            _cache.Remove(state.Id);
                        }
                        else
                        {
                            _cache.Update(state.Id, state);
                        }
                    }

                    if (_outboxNotificationDispatcher != null)
                    {
                        foreach (var notification in notifications)
                        {
                            _outboxNotificationDispatcher.NotifyCommandDispatched(notification);
                        }

                        // Wake the compensation worker immediately instead of waiting for its next DB sweep.
                        if (state.Status == WorkflowInstanceStatus.InError && HasWaitingCompensationWait(state.Waits))
                        {
                            _outboxNotificationDispatcher.NotifyCompensationRequired(state.Id);
                        }
                    }
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
        }

        private void CollectNewWaitsRecursive(IEnumerable<WaitInfrastructureDto> waits, List<WaitInfrastructureDto> result)
        {
            if (waits == null) return;
            foreach (var wait in waits)
            {
                if (!wait.IsPersisted)
                {
                    result.Add(wait);
                }
                if (wait.ChildWaits != null)
                {
                    CollectNewWaitsRecursive(wait.ChildWaits, result);
                }
            }
        }

        private static bool HasWaitingCompensationWait(IEnumerable<WaitInfrastructureDto>? waits)
        {
            if (waits == null) return false;
            foreach (var wait in waits)
            {
                if (wait is CompensationWaitDto compWait && compWait.Status == WaitStatus.Waiting)
                    return true;

                if (HasWaitingCompensationWait(wait.ChildWaits))
                    return true;
            }
            return false;
        }

        private void MarkWaitsAsPersistedRecursive(IEnumerable<WaitInfrastructureDto> waits)
        {
            if (waits == null) return;
            foreach (var wait in waits)
            {
                wait.IsPersisted = true;
                if (wait.ChildWaits != null)
                {
                    MarkWaitsAsPersistedRecursive(wait.ChildWaits);
                }
                if (wait is ExternalGroupWaitDto externalGroup)
                {
                    MarkWaitsAsPersistedRecursive(externalGroup.ExternalChildWaits);
                }
            }
        }

        private void CollectCancelledWaitsRecursive(
            IEnumerable<WaitInfrastructureDto> waits,
            HashSet<string> cancelledTokens,
            List<string> result)
        {
            if (waits == null) return;
            foreach (var wait in waits)
            {
                if (wait.CancelTokens != null && wait.CancelTokens.Any(t => cancelledTokens.Contains(t)))
                {
                    result.Add(wait.Id);
                }
                if (wait.ChildWaits != null)
                {
                    CollectCancelledWaitsRecursive(wait.ChildWaits, cancelledTokens, result);
                }
                if (wait is ExternalGroupWaitDto externalGroup)
                {
                    CollectCancelledWaitsRecursive(externalGroup.ExternalChildWaits, cancelledTokens, result);
                }
            }
        }

        private void FlattenAndCollectWaits(
            WaitInfrastructureDto wait,
            string? parentWaitId,
            Guid workflowInstanceId,
            List<WorkflowWaitEntity> resultList,
            List<ExternalChildWaitEntity> externalChildRecords)
        {
            if (wait == null) return;
            if (resultList.Any(r => r.Id == wait.Id)) return;

            if (wait is ExternalGroupWaitDto externalGroup)
            {
                // Parent external group is stored in the JSON state blob only.
                // Its children are persisted in the dedicated ExternalChildWaits table.
                if (externalGroup.ExternalChildWaits != null)
                {
                    foreach (var child in externalGroup.ExternalChildWaits)
                    {
                        FlattenAndCollectExternalChildWait(child, externalGroup.Id, workflowInstanceId, externalChildRecords);
                    }
                }
                return;
            }

            WorkflowWaitEntity record;
            if (wait is SignalWaitDto signalWait)
            {
                // SignalExactMatchPaths lives in the template cache — look it up by TemplateHashKey
                string signalExactMatchPaths = string.Empty;
                if (!string.IsNullOrEmpty(signalWait.TemplateHashKey))
                {
                    var template = _templateRepository?.GetTemplate(signalWait.TemplateHashKey);
                    if (template?.SignalExactMatchPathsJson != null && template.SignalExactMatchPathsJson != "[]")
                    {
                        var paths = System.Text.Json.JsonSerializer.Deserialize<List<string>>(template.SignalExactMatchPathsJson);
                        signalExactMatchPaths = paths != null ? string.Join(",", paths) : string.Empty;
                    }
                }

                record = new SignalWaitEntity
                {
                    SignalPath = signalWait.SignalIdentifier ?? string.Empty,
                    SignalExactMatchPaths = signalExactMatchPaths,
                    ExactMatchFilter = signalWait.ExactMatchPart ?? string.Empty,
                    IsFirstWait = signalWait.IsFirstWait,
                    TemplateHashKey = signalWait.TemplateHashKey
                };
            }
            else if (wait is TimeWaitDto timeWait)
            {
                record = new TimeWaitEntity
                {
                    UniqueMatchId = timeWait.UniqueMatchId ?? string.Empty,
                    ExecutionTime = timeWait.ExecutionTime
                };
            }
            else if (wait is CommandWaitDto commandWait)
            {
                record = new CommandWaitEntity
                {
                    CommandWaitId = commandWait.Id
                };
            }
            else if (wait is CompensationWaitDto compensationWait)
            {
                record = new CompensationWaitEntity
                {
                    Token = compensationWait.Token ?? string.Empty,
                    Attempts = compensationWait.Attempts,
                    LastError = compensationWait.LastError,
                    NextAttemptUtc = compensationWait.NextAttemptUtc
                };
            }
            else
            {
                record = new WorkflowWaitEntity();
            }

            record.Id = wait.Id;
            record.WorkflowInstanceId = workflowInstanceId;
            record.Status = (int)wait.Status;
            record.CancelTokens = wait.CancelTokens != null && wait.CancelTokens.Any()
                ? string.Join(",", wait.CancelTokens)
                : null;

            resultList.Add(record);

            if (wait.ChildWaits != null)
            {
                foreach (var child in wait.ChildWaits)
                {
                    FlattenAndCollectWaits(child, wait.Id, workflowInstanceId, resultList, externalChildRecords);
                }
            }
        }

        private void FlattenAndCollectExternalChildWait(
            WaitInfrastructureDto wait,
            string parentWaitId,
            Guid workflowInstanceId,
            List<ExternalChildWaitEntity> resultList)
        {
            if (wait == null) return;
            if (resultList.Any(r => r.Id == wait.Id)) return;

            string? signalExactMatchPaths = null;
            if (wait is SignalWaitDto signalWait && !string.IsNullOrEmpty(signalWait.TemplateHashKey))
            {
                var template = _templateRepository?.GetTemplate(signalWait.TemplateHashKey);
                if (template?.SignalExactMatchPathsJson != null && template.SignalExactMatchPathsJson != "[]")
                {
                    var paths = System.Text.Json.JsonSerializer.Deserialize<List<string>>(template.SignalExactMatchPathsJson);
                    signalExactMatchPaths = paths != null ? string.Join(",", paths) : null;
                }
            }

            var record = new ExternalChildWaitEntity
            {
                Id = wait.Id,
                WorkflowInstanceId = workflowInstanceId,
                ParentWaitId = parentWaitId,
                ChildWaitType = (int)wait.WaitType,
                Status = (int)wait.Status,
                SignalPath = wait is SignalWaitDto sw ? sw.SignalIdentifier : null,
                SignalExactMatchPaths = signalExactMatchPaths,
                ExactMatchFilter = wait is SignalWaitDto sw2 ? sw2.ExactMatchPart : null,
                IsFirstWait = wait is SignalWaitDto sw3 && sw3.IsFirstWait,
                TemplateHashKey = wait is SignalWaitDto sw4 ? sw4.TemplateHashKey : null,
                UniqueMatchId = wait is TimeWaitDto tw ? tw.UniqueMatchId : null,
                ExecutionTime = wait is TimeWaitDto tw2 ? tw2.ExecutionTime : null,
                CommandWaitId = wait is CommandWaitDto cw ? cw.Id : null,
                Token = wait is CompensationWaitDto cw2 ? cw2.Token : null,
                CancelTokens = wait.CancelTokens != null && wait.CancelTokens.Any()
                    ? string.Join(",", wait.CancelTokens)
                    : null,
                Created = wait.Created
            };

            resultList.Add(record);
        }

        public async Task<WorkflowStateDto> GetInstanceStateAsync(Guid instanceId)
        {
            if (_cache != null)
            {
                var cachedState = await _cache.GetOrAddAsync(instanceId, id => LoadInstanceStateFromDbAsync(id));
                return cachedState;
            }
            return await LoadInstanceStateFromDbAsync(instanceId);
        }

        private async Task<WorkflowStateDto> LoadInstanceStateFromDbAsync(Guid instanceId)
        {
            var dbInstance = await _dbContext.WorkflowInstances.FindAsync(instanceId);
            if (dbInstance == null) return null;

            return await MapToStateDtoAsync(dbInstance);
        }

        public async Task<List<WorkflowStateDto>> GetInstanceStatesAsync(IReadOnlyCollection<Guid> instanceIds)
        {
            var result = new List<WorkflowStateDto>();
            if (instanceIds == null || instanceIds.Count == 0) return result;

            var pending = new List<Guid>();
            var seen = new HashSet<Guid>();

            foreach (var instanceId in instanceIds)
            {
                if (!seen.Add(instanceId)) continue;

                if (_cache != null)
                {
                    // Warm entries win; the null factory means a miss is simply reported, not cached.
                    var cached = await _cache.GetOrAddAsync(instanceId, _ => Task.FromResult<WorkflowStateDto?>(null));
                    if (cached != null)
                    {
                        result.Add(cached);
                        continue;
                    }
                }

                pending.Add(instanceId);
            }

            if (pending.Count > 0)
            {
                var dbInstances = await _dbContext.WorkflowInstances
                    .Where(i => pending.Contains(i.Id))
                    .ToListAsync();

                foreach (var dbInstance in dbInstances)
                {
                    var state = await MapToStateDtoAsync(dbInstance);
                    _cache?.Update(state.Id, state);
                    result.Add(state);
                }
            }

            return result;
        }

        private async Task<WorkflowStateDto> MapToStateDtoAsync(WorkflowInstance dbInstance)
        {
            var waits = dbInstance.Waits ?? new();
            MarkWaitsAsPersistedRecursive(waits);
            await HydrateExternalChildWaitsAsync(_dbContext, dbInstance.Id, waits);

            return new WorkflowStateDto
            {
                Id = dbInstance.Id,
                Created = dbInstance.Created,
                Status = (WorkflowInstanceStatus)dbInstance.Status,
                WorkflowType = dbInstance.WorkflowType,
                WorkflowVersion = dbInstance.WorkflowVersion,
                StateObject = dbInstance.StateObject,
                Waits = waits,
                CancellationHistory = dbInstance.CancellationHistory,
                ErrorMessage = dbInstance.ErrorMessage
            };
        }

        internal static async Task HydrateExternalChildWaitsAsync(WorkflowsDbContext dbContext, Guid instanceId, List<WaitInfrastructureDto> waits)
        {
            if (waits == null) return;

            // Collect every external group in the tree first: one query for the whole instance instead of
            // one query per group (each fan-out level used to add a round trip).
            var groups = new List<ExternalGroupWaitDto>();
            CollectExternalGroupWaits(waits, groups);
            if (groups.Count == 0) return;

            var childEntities = await dbContext.ExternalChildWaits
                .Where(e => e.WorkflowInstanceId == instanceId)
                .ToListAsync();

            var byParent = childEntities.ToLookup(e => e.ParentWaitId);

            foreach (var externalGroup in groups)
            {
                externalGroup.ExternalChildWaits = byParent[externalGroup.Id].Select(MapToWaitDto).ToList();
            }
        }

        private static void CollectExternalGroupWaits(IEnumerable<WaitInfrastructureDto> waits, List<ExternalGroupWaitDto> groups)
        {
            foreach (var wait in waits)
            {
                if (wait is ExternalGroupWaitDto externalGroup)
                {
                    groups.Add(externalGroup);
                }

                if (wait.ChildWaits != null)
                {
                    CollectExternalGroupWaits(wait.ChildWaits, groups);
                }
            }
        }

        internal static WaitInfrastructureDto MapToWaitDto(ExternalChildWaitEntity entity)
        {
            WaitInfrastructureDto dto;
            if (entity.ExecutionTime.HasValue)
            {
                dto = new TimeWaitDto
                {
                    UniqueMatchId = entity.UniqueMatchId,
                    ExecutionTime = entity.ExecutionTime.GetValueOrDefault()
                };
            }
            else if (!string.IsNullOrEmpty(entity.CommandWaitId))
            {
                dto = new CommandWaitDto
                {
                    CommandData = null,
                    HandlerKey = string.Empty
                };
            }
            else if (!string.IsNullOrEmpty(entity.Token))
            {
                dto = new CompensationWaitDto
                {
                    Token = entity.Token
                };
            }
            else
            {
                dto = new SignalWaitDto
                {
                    SignalIdentifier = entity.SignalPath ?? string.Empty,
                    TemplateHashKey = entity.TemplateHashKey,
                    ExactMatchPart = entity.ExactMatchFilter
                };
            }

            dto.Id = entity.Id;
            dto.WaitType = (WaitType)entity.ChildWaitType;
            dto.Status = (WaitStatus)entity.Status;
            dto.Created = entity.Created;
            dto.IsPersisted = true;
            if (dto is SignalWaitDto signalWaitDto)
            {
                signalWaitDto.IsFirstWait = entity.IsFirstWait;
            }
            return dto;
        }

        public async Task<List<Guid>> FindInstancesWaitingForSignalAsync(string signalPath, string signalDataJson)
        {
            var matchedInstanceIds = new List<Guid>();

            // 1. Query TimeWaits for matching unique match ID (timer events)
            var matchedTimeWaits = await _dbContext.TimeWaits
                .Where(w => w.UniqueMatchId == signalPath && w.Status == (int)WaitStatus.Waiting)
                .Select(w => w.WorkflowInstanceId)
                .ToListAsync();
            matchedInstanceIds.AddRange(matchedTimeWaits);

            // 1b. Query external child time waits
            var matchedExternalTimeWaits = await _dbContext.ExternalChildWaits
                .Where(w => w.UniqueMatchId == signalPath && w.Status == (int)WaitStatus.Waiting)
                .Select(w => w.WorkflowInstanceId)
                .ToListAsync();
            matchedInstanceIds.AddRange(matchedExternalTimeWaits);

            // 2. Get distinct exact match path configurations for active waits of this signal path
            var rawMatchPaths = await _dbContext.SignalWaits
                .Where(w => w.SignalPath == signalPath && w.Status == (int)WaitStatus.Waiting)
                .Select(w => w.SignalExactMatchPaths)
                .Distinct()
                .ToListAsync();

            var externalRawMatchPaths = await _dbContext.ExternalChildWaits
                .Where(w => w.SignalPath == signalPath && w.Status == (int)WaitStatus.Waiting)
                .Select(w => w.SignalExactMatchPaths)
                .Distinct()
                .ToListAsync();

            var distinctMatchPaths = rawMatchPaths
                .Concat(externalRawMatchPaths)
                .Select(p => p ?? string.Empty)
                .Distinct()
                .ToList();

            if (distinctMatchPaths.Count > 0)
            {
                // Fallback: If signalDataJson is empty, retrieve all waiting instances for this signal path
                if (string.IsNullOrEmpty(signalDataJson))
                {
                    var allSignalMatched = await _dbContext.SignalWaits
                        .Where(w => w.SignalPath == signalPath && w.Status == (int)WaitStatus.Waiting)
                        .Select(w => w.WorkflowInstanceId)
                        .Distinct()
                        .ToListAsync();
                    matchedInstanceIds.AddRange(allSignalMatched);

                    var allExternalSignalMatched = await _dbContext.ExternalChildWaits
                        .Where(w => w.SignalPath == signalPath && w.Status == (int)WaitStatus.Waiting)
                        .Select(w => w.WorkflowInstanceId)
                        .Distinct()
                        .ToListAsync();
                    matchedInstanceIds.AddRange(allExternalSignalMatched);
                }
                else
                {
                    JToken? parsedToken = null;
                    try
                    {
                        parsedToken = JToken.Parse(signalDataJson);
                    }
                    catch (Exception)
                    {
                        // Fall back to null token without throwing
                    }

                    foreach (var pathsString in distinctMatchPaths)
                    {
                        if (string.IsNullOrEmpty(pathsString))
                        {
                            // Broadcast waits (no exact match requirements)
                            var broadcastIds = await _dbContext.SignalWaits
                                .Where(w => w.SignalPath == signalPath 
                                         && w.Status == (int)WaitStatus.Waiting 
                                         && (w.SignalExactMatchPaths == "" || w.SignalExactMatchPaths == null))
                                .Select(w => w.WorkflowInstanceId)
                                .ToListAsync();

                            matchedInstanceIds.AddRange(broadcastIds);

                            var externalBroadcastIds = await _dbContext.ExternalChildWaits
                                .Where(w => w.SignalPath == signalPath
                                         && w.Status == (int)WaitStatus.Waiting
                                         && (w.SignalExactMatchPaths == "" || w.SignalExactMatchPaths == null))
                                .Select(w => w.WorkflowInstanceId)
                                .ToListAsync();

                            matchedInstanceIds.AddRange(externalBroadcastIds);
                        }
                        else
                        {
                            // Extract the values from the incoming signal payload according to the path config
                            var paths = pathsString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            var values = new string[paths.Length];
                            for (int i = 0; i < paths.Length; i++)
                            {
                                JToken? token = null;
                                if (parsedToken != null)
                                {
                                    if (parsedToken is JContainer container)
                                    {
                                        try
                                        {
                                            token = container.SelectToken(paths[i]);
                                        }
                                        catch (Exception)
                                        {
                                            // Handle invalid paths gracefully
                                        }
                                    }
                                    else if (parsedToken is JValue && paths[i] == "$")
                                    {
                                        token = parsedToken;
                                    }
                                }
                                values[i] = FormatJToken(token);
                            }

                            // Serialize to JSON array to match ExactMatchFilter in DB
                            string calculatedFilter = System.Text.Json.JsonSerializer.Serialize(values);

                            var matchedIds = await _dbContext.SignalWaits
                                .Where(w => w.SignalPath == signalPath 
                                         && w.Status == (int)WaitStatus.Waiting 
                                         && w.SignalExactMatchPaths == pathsString 
                                         && w.ExactMatchFilter == calculatedFilter)
                                .Select(w => w.WorkflowInstanceId)
                                .ToListAsync();

                            matchedInstanceIds.AddRange(matchedIds);

                            var externalMatchedIds = await _dbContext.ExternalChildWaits
                                .Where(w => w.SignalPath == signalPath
                                         && w.Status == (int)WaitStatus.Waiting
                                         && w.SignalExactMatchPaths == pathsString
                                         && w.ExactMatchFilter == calculatedFilter)
                                .Select(w => w.WorkflowInstanceId)
                                .ToListAsync();

                            matchedInstanceIds.AddRange(externalMatchedIds);
                        }
                    }
                }
            }

            return matchedInstanceIds.Distinct().ToList();
        }

        /// <summary>
        /// Updates a wait row's status by ID in whichever concrete table owns it.
        /// </summary>
        private async Task UpdateWaitStatusAsync(string id, WaitStatus status)
        {
            var signal = await _dbContext.SignalWaits.FindAsync(id);
            if (signal != null)
            {
                signal.Status = (int)status;
                _dbContext.SignalWaits.Update(signal);
                return;
            }

            var command = await _dbContext.CommandWaits.FindAsync(id);
            if (command != null)
            {
                command.Status = (int)status;
                _dbContext.CommandWaits.Update(command);
                return;
            }

            var time = await _dbContext.TimeWaits.FindAsync(id);
            if (time != null)
            {
                time.Status = (int)status;
                _dbContext.TimeWaits.Update(time);
                return;
            }

            var externalChild = await _dbContext.ExternalChildWaits.FindAsync(id);
            if (externalChild != null)
            {
                externalChild.Status = (int)status;
                _dbContext.ExternalChildWaits.Update(externalChild);
                return;
            }
        }

        private WaitInfrastructureDto? FindWaitById(IEnumerable<WaitInfrastructureDto> waits, string id)
        {
            if (waits == null) return null;
            foreach (var wait in waits)
            {
                if (wait.Id == id) return wait;
                if (wait.ChildWaits != null)
                {
                    var found = FindWaitById(wait.ChildWaits, id);
                    if (found != null) return found;
                }
                if (wait is ExternalGroupWaitDto externalGroup)
                {
                    var found = FindWaitById(externalGroup.ExternalChildWaits, id);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>
        /// Inserts or updates a wait entity in the appropriate concrete table (TPC — no shared base table).
        /// </summary>
        private async Task UpsertWaitEntityAsync(WorkflowWaitEntity record)
        {
            if (record is SignalWaitEntity sigRecord)
            {
                var existing = await _dbContext.SignalWaits.FindAsync(sigRecord.Id);
                if (existing != null)
                {
                    existing.Status = sigRecord.Status;
                    existing.SignalPath = sigRecord.SignalPath;
                    existing.SignalExactMatchPaths = sigRecord.SignalExactMatchPaths;
                    existing.ExactMatchFilter = sigRecord.ExactMatchFilter;
                    existing.IsFirstWait = sigRecord.IsFirstWait;
                    existing.TemplateHashKey = sigRecord.TemplateHashKey;
                    _dbContext.SignalWaits.Update(existing);
                }
                else
                {
                    _dbContext.SignalWaits.Add(sigRecord);
                }
            }
            else if (record is CommandWaitEntity cmdRecord)
            {
                var existing = await _dbContext.CommandWaits.FindAsync(cmdRecord.Id);
                if (existing != null)
                {
                    existing.Status = cmdRecord.Status;
                    existing.CommandWaitId = cmdRecord.CommandWaitId;
                    _dbContext.CommandWaits.Update(existing);
                }
                else
                {
                    _dbContext.CommandWaits.Add(cmdRecord);
                }
            }
            else if (record is TimeWaitEntity timeRecord)
            {
                var existing = await _dbContext.TimeWaits.FindAsync(timeRecord.Id);
                if (existing != null)
                {
                    existing.Status = timeRecord.Status;
                    existing.UniqueMatchId = timeRecord.UniqueMatchId;
                    existing.ExecutionTime = timeRecord.ExecutionTime;
                    _dbContext.TimeWaits.Update(existing);
                }
                else
                {
                    _dbContext.TimeWaits.Add(timeRecord);
                }
            }
            else if (record is CompensationWaitEntity compRecord)
            {
                var existing = await _dbContext.CompensationWaits.FindAsync(compRecord.Id);
                if (existing != null)
                {
                    existing.Status = compRecord.Status;
                    existing.Token = compRecord.Token;
                    existing.Attempts = compRecord.Attempts;
                    existing.LastError = compRecord.LastError;
                    existing.NextAttemptUtc = compRecord.NextAttemptUtc;
                    _dbContext.CompensationWaits.Update(existing);
                }
                else
                {
                    _dbContext.CompensationWaits.Add(compRecord);
                }
            }
            // Base WorkflowWaitEntity (group/sub-workflow containers) — no concrete table, skip.
        }

        private async Task UpsertExternalChildWaitEntityAsync(ExternalChildWaitEntity record)
        {
            var existing = await _dbContext.ExternalChildWaits.FindAsync(record.Id);
            if (existing != null)
            {
                existing.Status = record.Status;
                existing.SignalPath = record.SignalPath;
                existing.SignalExactMatchPaths = record.SignalExactMatchPaths;
                existing.ExactMatchFilter = record.ExactMatchFilter;
                existing.IsFirstWait = record.IsFirstWait;
                existing.TemplateHashKey = record.TemplateHashKey;
                existing.UniqueMatchId = record.UniqueMatchId;
                existing.ExecutionTime = record.ExecutionTime;
                existing.CommandWaitId = record.CommandWaitId;
                existing.Token = record.Token;
                existing.CancelTokens = record.CancelTokens;
                _dbContext.ExternalChildWaits.Update(existing);
            }
            else
            {
                _dbContext.ExternalChildWaits.Add(record);
            }
        }

        private static string FormatJToken(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            if (token is JValue jValue)
            {
                var val = jValue.Value;
                if (val == null) return string.Empty;
                return Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            }

            return token.ToString();
        }

        public async Task<Guid> GetInstanceByCommandWaitIdAsync(string commandWaitId)
        {
            var record = await _dbContext.CommandWaits
                .FirstOrDefaultAsync(w => w.CommandWaitId == commandWaitId);
            if (record != null)
            {
                return record.WorkflowInstanceId;
            }

            var externalRecord = await _dbContext.ExternalChildWaits
                .FirstOrDefaultAsync(w => w.CommandWaitId == commandWaitId);
            return externalRecord?.WorkflowInstanceId ?? Guid.Empty;
        }

        public async Task<List<TimeWaitDto>> GetPendingTimeWaitsAsync()
        {
            var entities = await _dbContext.TimeWaits
                .Where(w => w.Status == (int)WaitStatus.Waiting)
                .ToListAsync();

            var externalEntities = await _dbContext.ExternalChildWaits
                .Where(w => w.Status == (int)WaitStatus.Waiting && w.ExecutionTime != null)
                .ToListAsync();

            var result = entities.Select(e => new TimeWaitDto
            {
                UniqueMatchId = e.UniqueMatchId,
                ExecutionTime = e.ExecutionTime
            }).ToList();

            result.AddRange(externalEntities.Select(e => new TimeWaitDto
            {
                UniqueMatchId = e.UniqueMatchId,
                ExecutionTime = e.ExecutionTime.GetValueOrDefault()
            }));

            return result;
        }

        private static bool AreWaitsEqual(List<WaitInfrastructureDto>? list1, List<WaitInfrastructureDto>? list2)
        {
            if (list1 == null && list2 == null) return true;
            if (list1 == null || list2 == null) return false;
            if (list1.Count != list2.Count) return false;

            for (int i = 0; i < list1.Count; i++)
            {
                if (!IsWaitEqual(list1[i], list2[i]))
                    return false;
            }

            return true;
        }

        private static bool IsWaitEqual(WaitInfrastructureDto w1, WaitInfrastructureDto w2)
        {
            if (w1 == null && w2 == null) return true;
            if (w1 == null || w2 == null) return false;

            if (w1.WaitType != w2.WaitType) return false;
            if (w1.WaitName != w2.WaitName) return false;
            if (w1.CallerName != w2.CallerName) return false;
            if (w1.InCodeLine != w2.InCodeLine) return false;
            if (w1.StateKey != w2.StateKey) return false;

            if (w1 is SignalWaitDto sw1 && w2 is SignalWaitDto sw2)
            {
                if (sw1.SignalIdentifier != sw2.SignalIdentifier) return false;
                if (sw1.ExactMatchPart != sw2.ExactMatchPart) return false;
            }
            else if (w1 is TimeWaitDto tw1 && w2 is TimeWaitDto tw2)
            {
                if (tw1.UniqueMatchId != tw2.UniqueMatchId) return false;
                if (Math.Abs((tw1.ExecutionTime - tw2.ExecutionTime).TotalSeconds) > 5) return false;
            }
            else if (w1 is CommandWaitDto cw1 && w2 is CommandWaitDto cw2)
            {
                if (cw1.HandlerKey != cw2.HandlerKey) return false;
                if (cw1.CommandData != cw2.CommandData) return false;
            }
            else if (w1 is GroupWaitDto gw1 && w2 is GroupWaitDto gw2)
            {
                if (gw1.MatchFuncName != gw2.MatchFuncName) return false;
                if (!AreWaitsEqual(gw1.ChildWaits, gw2.ChildWaits)) return false;
            }
            else if (w1 is SubWorkflowWaitDto sub1 && w2 is SubWorkflowWaitDto sub2)
            {
                if (!AreWaitsEqual(sub1.ChildWaits, sub2.ChildWaits)) return false;
            }

            return true;
        }

        private static bool HasFirstWait(List<WaitInfrastructureDto>? waits)
        {
            if (waits == null) return false;
            foreach (var w in waits)
            {
                if (w is SignalWaitDto sw && sw.IsFirstWait) return true;
                if (w.ChildWaits != null && HasFirstWait(w.ChildWaits)) return true;
                if (w is ExternalGroupWaitDto externalGroup && HasFirstWait(externalGroup.ExternalChildWaits)) return true;
            }
            return false;
        }

        private void CollectAndInsertOutboxMessagesRecursive(WaitInfrastructureDto wait, Guid workflowInstanceId, List<CommandDispatchNotification> notifications)
        {
            if (wait == null) return;
            if (wait is CommandWaitDto commandWait && 
                commandWait.Status == WaitStatus.Waiting)
            {
                bool alreadyExists = _dbContext.OutboxMessages.Local.Any(m => m.CommandWaitId == commandWait.Id) ||
                                     _dbContext.OutboxMessages.Any(m => m.CommandWaitId == commandWait.Id);
                if (!alreadyExists)
                {
                    var notification = new CommandDispatchNotification
                    {
                        CommandWaitId = commandWait.Id,
                        HandlerKey = commandWait.HandlerKey,
                        CommandData = commandWait.CommandData?.ToString() ?? string.Empty
                    };

                    var outboxMessage = new OutboxMessageEntity
                    {
                        GlobalId = Guid.NewGuid(),
                        CreatedAt = DateTime.UtcNow,
                        Status = 0, // Pending
                        MessageType = typeof(CommandDispatchNotification).AssemblyQualifiedName ?? typeof(CommandDispatchNotification).FullName ?? "CommandDispatchNotification",
                        Payload = JsonConvert.SerializeObject(notification),
                        WorkflowInstanceId = workflowInstanceId,
                        CommandWaitId = commandWait.Id
                    };

                    _dbContext.OutboxMessages.Add(outboxMessage);

                    if (_outboxNotificationDispatcher != null)
                    {
                        notifications.Add(notification);
                    }
                }
            }

            if (wait.ChildWaits != null)
            {
                foreach (var child in wait.ChildWaits)
                {
                    CollectAndInsertOutboxMessagesRecursive(child, workflowInstanceId, notifications);
                }
            }

            if (wait is ExternalGroupWaitDto externalGroup && externalGroup.ExternalChildWaits != null)
            {
                foreach (var child in externalGroup.ExternalChildWaits)
                {
                    CollectAndInsertOutboxMessagesRecursive(child, workflowInstanceId, notifications);
                }
            }
        }

        public async Task ReplaceMigratedStateAsync(
            Guid instanceId,
            WorkflowStateDto newState,
            List<WaitInfrastructureDto> newWaits,
            int newVersion,
            System.Threading.CancellationToken ct)
        {
            using (var transaction = await _dbContext.Database.BeginTransactionAsync(ct))
            {
                try
                {
                    var dbInstance = await _dbContext.WorkflowInstances.FindAsync(new object[] { instanceId }, ct)
                        ?? throw new InvalidOperationException($"Workflow instance '{instanceId}' not found.");

                    dbInstance.WorkflowVersion = newVersion;
                    // A migration never changes the workflow type, and a snapshot restore may not carry it.
                    if (!string.IsNullOrEmpty(newState.WorkflowType))
                    {
                        dbInstance.WorkflowType = newState.WorkflowType;
                    }
                    dbInstance.Status = (int)newState.Status;
                    dbInstance.StateObject = newState.StateObject ?? new();
                    dbInstance.CancellationHistory = newState.CancellationHistory ?? new();
                    dbInstance.Waits = newWaits;
                    dbInstance.ConcurrencyToken = Guid.NewGuid().ToString();

                    var oldSignalWaits = await _dbContext.SignalWaits.Where(w => w.WorkflowInstanceId == instanceId).ToListAsync(ct);
                    _dbContext.SignalWaits.RemoveRange(oldSignalWaits);

                    var oldCommandWaits = await _dbContext.CommandWaits.Where(w => w.WorkflowInstanceId == instanceId).ToListAsync(ct);
                    _dbContext.CommandWaits.RemoveRange(oldCommandWaits);

                    var oldTimeWaits = await _dbContext.TimeWaits.Where(w => w.WorkflowInstanceId == instanceId).ToListAsync(ct);
                    _dbContext.TimeWaits.RemoveRange(oldTimeWaits);

                    var oldExternalChildWaits = await _dbContext.ExternalChildWaits.Where(w => w.WorkflowInstanceId == instanceId).ToListAsync(ct);
                    _dbContext.ExternalChildWaits.RemoveRange(oldExternalChildWaits);

                    MarkWaitsAsPersistedRecursive(newWaits);

                    var flattenedRecords = new List<WorkflowWaitEntity>();
                    var externalChildRecords = new List<ExternalChildWaitEntity>();
                    foreach (var wait in newWaits)
                    {
                        FlattenAndCollectWaits(wait, null, instanceId, flattenedRecords, externalChildRecords);
                    }

                    foreach (var record in flattenedRecords)
                    {
                        if (record is SignalWaitEntity sigRecord)
                        {
                            _dbContext.SignalWaits.Add(sigRecord);
                        }
                        else if (record is CommandWaitEntity cmdRecord)
                        {
                            _dbContext.CommandWaits.Add(cmdRecord);
                        }
                        else if (record is TimeWaitEntity timeRecord)
                        {
                            _dbContext.TimeWaits.Add(timeRecord);
                        }
                    }

                    foreach (var record in externalChildRecords)
                    {
                        _dbContext.ExternalChildWaits.Add(record);
                    }

                    _dbContext.WorkflowInstances.Update(dbInstance);

                    await _dbContext.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    _cache?.Remove(instanceId);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(ct);
                    throw;
                }
            }
        }
        public async Task<bool> HasSignalBeenProcessedAsync(Guid messageId)
        {
            var msgIdStr = messageId.ToString();
            return await _dbContext.SignalInbox.AnyAsync(x => x.MessageId == msgIdStr);
        }

        public async Task PruneProcessedSignalsAsync(DateTime threshold)
        {
            var oldRecords = await _dbContext.SignalInbox
                .Where(x => x.ProcessedAt < threshold)
                .ToListAsync();
            if (oldRecords.Any())
            {
                _dbContext.SignalInbox.RemoveRange(oldRecords);
                await _dbContext.SaveChangesAsync();
            }
        }

        #region Denormalized Admin Columns Helpers

        private static int CountActiveWaitsRecursive(IEnumerable<WaitInfrastructureDto>? waits)
        {
            if (waits == null) return 0;
            int count = 0;
            foreach (var wait in waits)
            {
                if (wait.Status == WaitStatus.Waiting)
                    count++;

                if (wait.ChildWaits != null)
                    count += CountActiveWaitsRecursive(wait.ChildWaits);

                if (wait is ExternalGroupWaitDto externalGroup && externalGroup.ExternalChildWaits != null)
                    count += CountActiveWaitsRecursive(externalGroup.ExternalChildWaits);
            }
            return count;
        }

        private static string? BuildLastAdvanceReason(IEnumerable<WaitInfrastructureDto>? waits, Guid? triggeringSignalId)
        {
            if (triggeringSignalId.HasValue)
                return $"Signal: {triggeringSignalId.Value}";

            var matchedWait = FindMostRecentlyMatchedWait(waits);
            if (matchedWait == null) return null;

            return matchedWait switch
            {
                SignalWaitDto sw => $"Signal: {sw.SignalIdentifier}",
                CommandWaitDto cw => $"Command: {cw.HandlerKey}",
                TimeWaitDto tw => $"Timer: {tw.UniqueMatchId}",
                CompensationWaitDto cw => $"Compensation: {cw.Token}",
                _ => $"Wait: {matchedWait.WaitName}"
            };
        }

        private static WaitInfrastructureDto? FindMostRecentlyMatchedWait(IEnumerable<WaitInfrastructureDto>? waits)
        {
            if (waits == null) return null;
            WaitInfrastructureDto? candidate = null;
            foreach (var wait in waits)
            {
                if (wait.Status == WaitStatus.Matched || wait.Status == WaitStatus.Completed)
                {
                    if (candidate == null || wait.Created > candidate.Created)
                        candidate = wait;
                }

                var childCandidate = FindMostRecentlyMatchedWait(wait.ChildWaits);
                if (childCandidate != null && (candidate == null || childCandidate.Created > candidate.Created))
                    candidate = childCandidate;

                if (wait is ExternalGroupWaitDto externalGroup)
                {
                    var externalCandidate = FindMostRecentlyMatchedWait(externalGroup.ExternalChildWaits);
                    if (externalCandidate != null && (candidate == null || externalCandidate.Created > candidate.Created))
                        candidate = externalCandidate;
                }
            }
            return candidate;
        }

        private static string? ExtractErrorMessage(WorkflowStateDto state)
        {
            // Explicit error propagated by the state (e.g. a failed compensation attempt) wins.
            if (!string.IsNullOrEmpty(state.ErrorMessage))
                return state.ErrorMessage;

            if (state.Status != WorkflowInstanceStatus.InError
                && state.Status != WorkflowInstanceStatus.CompensationFailed)
                return null;

            // Try to extract an error message from the state object's instance if available.
            var instance = state.StateObject?.Instance;
            if (instance == null) return null;

            var errorProperty = instance.GetType().GetProperty("ErrorMessage")
                ?? instance.GetType().GetProperty("Error");
            if (errorProperty != null)
            {
                var value = errorProperty.GetValue(instance);
                if (value != null)
                    return value.ToString();
            }

            return null;
        }

        private static bool IsCompletedStatus(WorkflowInstanceStatus status)
        {
            return status == WorkflowInstanceStatus.Completed
                || status == WorkflowInstanceStatus.InError
                || status == WorkflowInstanceStatus.CompensationFailed
                || status == WorkflowInstanceStatus.Canceled;
        }

        public async Task AppendDlqMessageAsync(DlqMessage message, System.Threading.CancellationToken ct = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            _dbContext.DlqMessages.Add(ToDlqMessageEntity(message));

            await _dbContext.SaveChangesAsync(ct);
        }

        public async Task SaveInstanceSnapshotAsync(WorkflowInstanceSnapshot snapshot, System.Threading.CancellationToken ct = default)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            _dbContext.WorkflowInstanceSnapshots.Add(new WorkflowInstanceSnapshotEntity
            {
                Id = string.IsNullOrEmpty(snapshot.Id) ? Guid.NewGuid().ToString("N") : snapshot.Id,
                WorkflowInstanceId = snapshot.InstanceId,
                WorkflowType = snapshot.WorkflowType ?? string.Empty,
                FromVersion = snapshot.FromVersion,
                ToVersion = snapshot.ToVersion,
                Reason = snapshot.Reason ?? string.Empty,
                StateJson = snapshot.StateJson ?? string.Empty,
                CreatedAt = snapshot.CreatedAt == default ? DateTime.UtcNow : snapshot.CreatedAt
            });

            await _dbContext.SaveChangesAsync(ct);
        }

        public async Task<List<WorkflowInstanceSnapshot>> GetInstanceSnapshotsAsync(Guid instanceId, System.Threading.CancellationToken ct = default)
        {
            return await _dbContext.WorkflowInstanceSnapshots
                .Where(s => s.WorkflowInstanceId == instanceId)
                .OrderBy(s => s.CreatedAt)
                .Select(s => new WorkflowInstanceSnapshot
                {
                    Id = s.Id,
                    InstanceId = s.WorkflowInstanceId,
                    WorkflowType = s.WorkflowType,
                    FromVersion = s.FromVersion,
                    ToVersion = s.ToVersion,
                    Reason = s.Reason,
                    StateJson = s.StateJson,
                    CreatedAt = s.CreatedAt
                })
                .ToListAsync(ct);
        }

        public async Task<bool> RestoreInstanceSnapshotAsync(string snapshotId, System.Threading.CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(snapshotId)) return false;

            var snapshot = await _dbContext.WorkflowInstanceSnapshots
                .FirstOrDefaultAsync(s => s.Id == snapshotId, ct);

            if (snapshot == null) return false;

            var state = _serializer.Deserialize<WorkflowStateDto>(
                snapshot.StateJson,
                SerializationScope.CompilerGeneratedClass);

            await ReplaceMigratedStateAsync(
                snapshot.WorkflowInstanceId,
                state,
                state.Waits ?? new List<WaitInfrastructureDto>(),
                snapshot.FromVersion,
                ct);

            return true;
        }

        public async Task<Dictionary<string, int>> GetParkedInstanceCountsByDllVersionAsync(
            System.Threading.CancellationToken ct = default)
        {
            var capabilities = await _dbContext.WorkerCapabilities
                .Where(c => c.IsActive)
                .ToListAsync(ct);

            var parkedInstances = await _dbContext.WorkflowInstances
                .Where(i => i.Status == (int)WorkflowInstanceStatus.CompensationFailed
                         || i.Status == (int)WorkflowInstanceStatus.MigrationFailed)
                .Select(i => new { i.WorkflowType, i.WorkflowVersion })
                .ToListAsync(ct);

            var instanceCounts = parkedInstances
                .GroupBy(i => new { i.WorkflowType, i.WorkflowVersion })
                .ToDictionary(g => g.Key, g => g.Count());

            var dllCounts = new Dictionary<string, int>();
            foreach (var group in capabilities.GroupBy(c => c.DllVersion))
            {
                int total = 0;
                foreach (var cap in group)
                {
                    if (instanceCounts.TryGetValue(new { WorkflowType = cap.WorkflowType, WorkflowVersion = cap.WorkflowVersion }, out var count))
                    {
                        total += count;
                    }
                }
                dllCounts[group.Key] = total;
            }

            return dllCounts;
        }

        private static DlqMessageEntity ToDlqMessageEntity(DlqMessage message)
        {
            return new DlqMessageEntity
            {
                WorkflowInstanceId = message.InstanceId,
                WorkflowType = message.WorkflowType ?? string.Empty,
                FailureCount = message.FailureCount,
                PayloadJson = message.PayloadJson,
                ErrorMessage = message.ErrorMessage,
                CreatedAt = message.FailedAt == default ? DateTime.UtcNow : message.FailedAt,
                Status = 0
            };
        }

        public async Task<bool> ResetCompensationAsync(Guid instanceId, System.Threading.CancellationToken ct = default)
        {
            var dbInstance = await _dbContext.WorkflowInstances
                .FirstOrDefaultAsync(i => i.Id == instanceId, ct);

            if (dbInstance == null) return false;

            // Idempotent guard: only instances that are stuck mid-rollback can be retried.
            if ((WorkflowInstanceStatus)dbInstance.Status != WorkflowInstanceStatus.CompensationFailed)
                return false;

            var resetCount = ResetCompensationWaitsRecursive(dbInstance.Waits);
            if (resetCount == 0) return false;

            dbInstance.Status = (int)WorkflowInstanceStatus.InError;
            dbInstance.ErrorMessage = null;
            dbInstance.CompletedAt = null;
            dbInstance.Modified = DateTime.UtcNow;
            dbInstance.LastAdvanceReason = "Compensation retry requested";
            dbInstance.ConcurrencyToken = Guid.NewGuid().ToString();
            dbInstance.ActiveWaitCount = CountActiveWaitsRecursive(dbInstance.Waits);

            var compensationRows = await _dbContext.CompensationWaits
                .Where(w => w.WorkflowInstanceId == instanceId)
                .ToListAsync(ct);

            foreach (var row in compensationRows)
            {
                row.Status = (int)WaitStatus.Waiting;
                row.Attempts = 0;
                row.LastError = null;
                row.NextAttemptUtc = null;
            }

            _dbContext.WorkflowInstances.Update(dbInstance);
            await _dbContext.SaveChangesAsync(ct);

            _cache?.Remove(instanceId);
            _outboxNotificationDispatcher?.NotifyCompensationRequired(instanceId);
            return true;
        }

        private static int ResetCompensationWaitsRecursive(IEnumerable<WaitInfrastructureDto>? waits)
        {
            if (waits == null) return 0;

            var count = 0;
            foreach (var wait in waits)
            {
                if (wait is CompensationWaitDto compWait)
                {
                    compWait.Status = WaitStatus.Waiting;
                    compWait.Attempts = 0;
                    compWait.LastError = null;
                    compWait.NextAttemptUtc = null;
                    count++;
                }

                count += ResetCompensationWaitsRecursive(wait.ChildWaits);

                if (wait is ExternalGroupWaitDto externalGroup)
                    count += ResetCompensationWaitsRecursive(externalGroup.ExternalChildWaits);
            }
            return count;
        }

        public async Task RegisterWorkerCapabilitiesAsync(
            string dllVersion,
            IEnumerable<(string workflowType, int workflowVersion)> capabilities,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(dllVersion) || capabilities == null) return;

            foreach (var (workflowType, workflowVersion) in capabilities)
            {
                var existing = await _dbContext.WorkerCapabilities.FirstOrDefaultAsync(
                    c => c.DllVersion == dllVersion && c.WorkflowType == workflowType && c.WorkflowVersion == workflowVersion, ct);

                if (existing == null)
                {
                    _dbContext.WorkerCapabilities.Add(new WorkerCapabilityEntity
                    {
                        DllVersion = dllVersion,
                        WorkflowType = workflowType,
                        WorkflowVersion = workflowVersion,
                        RegisteredAt = DateTime.UtcNow,
                        IsActive = true
                    });
                }
                else if (!existing.IsActive)
                {
                    existing.IsActive = true;
                    existing.RegisteredAt = DateTime.UtcNow;
                    _dbContext.WorkerCapabilities.Update(existing);
                }
            }

            await _dbContext.SaveChangesAsync(ct);
        }

        public async Task<List<string>> GetActiveDllVersionsForWorkflowAsync(
            string workflowType,
            int workflowVersion,
            CancellationToken ct = default)
        {
            return await _dbContext.WorkerCapabilities
                .Where(c => c.WorkflowType == workflowType && c.WorkflowVersion == workflowVersion && c.IsActive)
                .Select(c => c.DllVersion)
                .Distinct()
                .ToListAsync(ct);
        }

        public async Task<Dictionary<string, int>> GetActiveInstanceCountsByDllVersionAsync(
            CancellationToken ct = default)
        {
            var capabilities = await _dbContext.WorkerCapabilities
                .Where(c => c.IsActive)
                .ToListAsync(ct);

            // "Active" for drain purposes includes instances whose rollback has not finished yet: a
            // waiting compensation still needs a worker able to run the compensation handler for that
            // workflow version. Shutting the last worker of that version down would strand the saga.
            var activeInstances = await _dbContext.WorkflowInstances
                .Where(i =>
                    i.Status == (int)WorkflowInstanceStatus.Running
                    || i.Status == (int)WorkflowInstanceStatus.InProgress
                    || i.Status == (int)WorkflowInstanceStatus.New
                    || (i.Status == (int)WorkflowInstanceStatus.InError
                        && _dbContext.CompensationWaits.Any(w => w.WorkflowInstanceId == i.Id && w.Status == (int)WaitStatus.Waiting)))
                .Select(i => new { i.WorkflowType, i.WorkflowVersion })
                .ToListAsync(ct);


            var instanceCounts = activeInstances
                .GroupBy(i => new { i.WorkflowType, i.WorkflowVersion })
                .ToDictionary(g => g.Key, g => g.Count());

            var dllCounts = new Dictionary<string, int>();

            var groupedByDll = capabilities.GroupBy(c => c.DllVersion);
            foreach (var group in groupedByDll)
            {
                int totalActiveForDll = 0;
                foreach (var cap in group)
                {
                    var key = new { WorkflowType = cap.WorkflowType, WorkflowVersion = cap.WorkflowVersion };
                    if (instanceCounts.TryGetValue(key, out var count))
                    {
                        totalActiveForDll += count;
                    }
                }
                dllCounts[group.Key] = totalActiveForDll;
            }

            return dllCounts;
        }

        public async Task<List<TimeWaitDto>> ClaimDueTimersAsync(int batchSize, string nodeId, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var dueEntities = await _dbContext.TimeWaits
                .Where(w => w.Status == (int)WaitStatus.Waiting && w.ExecutionTime <= now)
                .OrderBy(w => w.ExecutionTime)
                .Take(batchSize)
                .ToListAsync(ct);

            if (dueEntities.Count == 0)
                return new List<TimeWaitDto>();

            var claimedDtos = new List<TimeWaitDto>();

            foreach (var entity in dueEntities)
            {
                entity.Status = (int)WaitStatus.Matched;
                _dbContext.TimeWaits.Update(entity);

                claimedDtos.Add(new TimeWaitDto
                {
                    Id = entity.Id.ToString(),
                    UniqueMatchId = entity.UniqueMatchId,
                    ExecutionTime = entity.ExecutionTime
                });
            }

            await _dbContext.SaveChangesAsync(ct);
            return claimedDtos;
        }

        public async Task RecoverStaleTimersAsync(TimeSpan staleThreshold, CancellationToken ct = default)
        {
            var cutoff = DateTime.UtcNow.Subtract(staleThreshold);
            var staleTimers = await _dbContext.TimeWaits
                .Where(w => w.Status == (int)WaitStatus.Matched && w.ExecutionTime <= cutoff)
                .ToListAsync(ct);

            if (staleTimers.Count > 0)
            {
                foreach (var timer in staleTimers)
                {
                    timer.Status = (int)WaitStatus.Waiting;
                    _dbContext.TimeWaits.Update(timer);
                }
                await _dbContext.SaveChangesAsync(ct);
            }
        }

        #endregion
    }
}


