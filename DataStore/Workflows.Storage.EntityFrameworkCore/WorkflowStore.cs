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
            Guid? triggeringSignalId = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            // Extract new waits that are not yet persisted before we change their flag to true
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

                        foreach (var candidate in candidates)
                        {
                            if (HasFirstWait(candidate.Waits))
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
                            Waits = state.Waits ?? new()
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

                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    if (_cache != null)
                    {
                        Console.WriteLine($"[CACHE DEBUG] SaveContextSyncAsync: State ID = {state.Id}, Status = {state.Status}");
                        if (state.Status == WorkflowInstanceStatus.Completed || state.Status == WorkflowInstanceStatus.InError)
                        {
                            Console.WriteLine($"[CACHE DEBUG] Removing from cache: {state.Id}");
                            _cache.Remove(state.Id);
                        }
                        else
                        {
                            Console.WriteLine($"[CACHE DEBUG] Updating cache: {state.Id} to Status {state.Status}");
                            _cache.Update(state.Id, state);
                        }
                    }

                    if (_outboxNotificationDispatcher != null)
                    {
                        foreach (var notification in notifications)
                        {
                            _outboxNotificationDispatcher.NotifyCommandDispatched(notification);
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
                if (wait is ExternalGroupWaitDto externalGroup)
                {
                    CollectNewWaitsRecursive(externalGroup.ExternalChildWaits, result);
                }
            }
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
                    Token = compensationWait.Token ?? string.Empty
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
                var cachedState = await _cache.GetOrAddAsync(instanceId, async id => {
                    Console.WriteLine($"[CACHE DEBUG] GetInstanceStateAsync: Cache MISS for {id}. Loading from DB. Stack Trace:\n{Environment.StackTrace}");
                    return await LoadInstanceStateFromDbAsync(id);
                });
                if (cachedState != null)
                {
                    Console.WriteLine($"[CACHE DEBUG] GetInstanceStateAsync: Cache HIT for {instanceId}. Status = {cachedState.Status}");
                    return cachedState;
                }
                return null;
            }
            Console.WriteLine($"[CACHE DEBUG] GetInstanceStateAsync: Cache is NULL. Loading from DB.");
            return await LoadInstanceStateFromDbAsync(instanceId);
        }

        private async Task<WorkflowStateDto> LoadInstanceStateFromDbAsync(Guid instanceId)
        {
            var dbInstance = await _dbContext.WorkflowInstances.FindAsync(instanceId);
            if (dbInstance == null) return null;

            var waits = dbInstance.Waits ?? new();
            MarkWaitsAsPersistedRecursive(waits);
            await HydrateExternalChildWaitsAsync(instanceId, waits);

            return new WorkflowStateDto
            {
                Id = dbInstance.Id,
                Created = dbInstance.Created,
                Status = (WorkflowInstanceStatus)dbInstance.Status,
                WorkflowType = dbInstance.WorkflowType,
                WorkflowVersion = dbInstance.WorkflowVersion,
                StateObject = dbInstance.StateObject,
                Waits = waits,
                CancellationHistory = dbInstance.CancellationHistory
            };
        }

        private async Task HydrateExternalChildWaitsAsync(Guid instanceId, List<WaitInfrastructureDto> waits)
        {
            if (waits == null) return;
            foreach (var wait in waits)
            {
                if (wait is ExternalGroupWaitDto externalGroup)
                {
                    var childEntities = await _dbContext.ExternalChildWaits
                        .Where(e => e.WorkflowInstanceId == instanceId && e.ParentWaitId == externalGroup.Id)
                        .ToListAsync();

                    externalGroup.ExternalChildWaits = childEntities.Select(MapToWaitDto).ToList();
                }

                if (wait.ChildWaits != null)
                {
                    await HydrateExternalChildWaitsAsync(instanceId, wait.ChildWaits);
                }
            }
        }

        private static WaitInfrastructureDto MapToWaitDto(ExternalChildWaitEntity entity)
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
                    dbInstance.WorkflowType = newState.WorkflowType;
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
    }
}
