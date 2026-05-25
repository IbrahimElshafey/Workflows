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

        public WorkflowStore(WorkflowsDbContext dbContext, IObjectSerializer serializer)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public async Task SaveContextSyncAsync(
            WorkflowStateDto state,
            IEnumerable<Guid> completedWaitIds)
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
                    // 1. Update/Create WorkflowInstance
                    var dbInstance = await _dbContext.WorkflowInstances.FindAsync(state.Id);
                    if (dbInstance == null)
                    {
                        dbInstance = new WorkflowInstance
                        {
                            Id = state.Id,
                            Created = state.Created,
                            Status = (int)state.Status,
                            WorkflowType = state.WorkflowType,
                            StateObject = state.StateObject ?? new(),
                            CancellationHistory = state.CancellationHistory ?? new(),
                            Waits = state.Waits ?? new()
                        };
                        _dbContext.WorkflowInstances.Add(dbInstance);
                    }
                    else
                    {
                        dbInstance.Status = (int)state.Status;
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
                        foreach (var wait in newWaitsList)
                        {
                            FlattenAndCollectWaits(wait, null, state.Id, flattenedRecords);
                        }

                        foreach (var record in flattenedRecords)
                        {
                            await UpsertWaitEntityAsync(record);
                        }
                    }

                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();
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
            }
        }

        private void CollectCancelledWaitsRecursive(
            IEnumerable<WaitInfrastructureDto> waits,
            HashSet<string> cancelledTokens,
            List<Guid> result)
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
            }
        }

        private void FlattenAndCollectWaits(
            WaitInfrastructureDto wait,
            Guid? parentWaitId,
            Guid workflowInstanceId,
            List<WorkflowWaitEntity> resultList)
        {
            if (wait == null) return;
            if (resultList.Any(r => r.Id == wait.Id)) return;

            WorkflowWaitEntity record;
            if (wait is SignalWaitDto signalWait)
            {
                record = new SignalWaitEntity
                {
                    SignalPath = signalWait.SignalIdentifier ?? string.Empty,
                    SignalExactMatchPaths = signalWait.SignalExactMatchPaths != null ? string.Join(",", signalWait.SignalExactMatchPaths) : string.Empty,
                    ExactMatchFilter = signalWait.ExactMatchPart ?? string.Empty,
                    IsFirstWait = signalWait.IsFirstWait
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
            else
            {
                record = new WorkflowWaitEntity();
            }

            record.Id = wait.Id;
            record.WorkflowInstanceId = workflowInstanceId;
            record.Status = (int)wait.Status;

            resultList.Add(record);

            if (wait.ChildWaits != null)
            {
                foreach (var child in wait.ChildWaits)
                {
                    FlattenAndCollectWaits(child, wait.Id, workflowInstanceId, resultList);
                }
            }
        }

        public async Task<WorkflowStateDto> GetInstanceStateAsync(Guid instanceId)
        {
            var dbInstance = await _dbContext.WorkflowInstances.FindAsync(instanceId);
            if (dbInstance == null) return null;

            var waits = dbInstance.Waits ?? new();
            MarkWaitsAsPersistedRecursive(waits);

            return new WorkflowStateDto
            {
                Id = dbInstance.Id,
                Created = dbInstance.Created,
                Status = (WorkflowInstanceStatus)dbInstance.Status,
                WorkflowType = dbInstance.WorkflowType,
                StateObject = dbInstance.StateObject,
                Waits = waits,
                CancellationHistory = dbInstance.CancellationHistory
            };
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

            // 2. Get distinct exact match path configurations for active waits of this signal path
            var distinctMatchPaths = await _dbContext.SignalWaits
                .Where(w => w.SignalPath == signalPath && w.Status == (int)WaitStatus.Waiting)
                .Select(w => w.SignalExactMatchPaths)
                .Distinct()
                .ToListAsync();

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
                }
                else
                {
                    var jObject = JObject.Parse(signalDataJson);

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
                        }
                        else
                        {
                            // Extract the values from the incoming signal payload according to the path config
                            var paths = pathsString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            var values = new string[paths.Length];
                            for (int i = 0; i < paths.Length; i++)
                            {
                                var token = jObject.SelectToken(paths[i]);
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
                        }
                    }
                }
            }

            return matchedInstanceIds.Distinct().ToList();
        }

        /// <summary>
        /// Updates a wait row's status by ID in whichever concrete table owns it.
        /// </summary>
        private async Task UpdateWaitStatusAsync(Guid id, WaitStatus status)
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
            }
        }

        private WaitInfrastructureDto? FindWaitById(IEnumerable<WaitInfrastructureDto> waits, Guid id)
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

        public async Task<Guid> GetInstanceByCommandWaitIdAsync(Guid commandWaitId)
        {
            var record = await _dbContext.CommandWaits
                .FirstOrDefaultAsync(w => w.CommandWaitId == commandWaitId);
            return record?.WorkflowInstanceId ?? Guid.Empty;
        }
    }
}
