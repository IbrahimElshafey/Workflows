using Microsoft.EntityFrameworkCore;
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

namespace Workflows.Orchestrator.Data.EF
{
    public class EfWorkflowStore : IWorkflowStore
    {
        private readonly WorkflowsDbContext _dbContext;
        private readonly IObjectSerializer _serializer;

        public EfWorkflowStore(WorkflowsDbContext dbContext, IObjectSerializer serializer)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public async Task SaveContextSyncAsync(
            WorkflowStateDto state,
            IEnumerable<WaitInfrastructureDto> newWaits,
            IEnumerable<Guid> completedWaitIds)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            using (var transaction = await _dbContext.Database.BeginTransactionAsync())
            {
                try
                {
                    // 1. Update/Create WorkflowState
                    var dbState = await _dbContext.WorkflowStates.FindAsync(state.Id);
                    var stateJson = _serializer.Serialize(state.StateObject, SerializationScope.CompilerGeneratedClass).ToString();
                    var cancelJson = _serializer.Serialize(state.CancellationHistory, SerializationScope.Standard).ToString();

                    if (dbState == null)
                    {
                        dbState = new DbWorkflowState
                        {
                            Id = state.Id,
                            Created = state.Created,
                            Status = (int)state.Status,
                            WorkflowType = state.WorkflowType,
                            StateObjectJson = stateJson,
                            CancellationHistoryJson = cancelJson
                        };
                        _dbContext.WorkflowStates.Add(dbState);
                    }
                    else
                    {
                        dbState.Status = (int)state.Status;
                        dbState.StateObjectJson = stateJson;
                        dbState.CancellationHistoryJson = cancelJson;
                        _dbContext.WorkflowStates.Update(dbState);
                    }

                    // 2. Delete completed waits
                    if (completedWaitIds != null)
                    {
                        foreach (var id in completedWaitIds)
                        {
                            var existing = await _dbContext.WaitRecords.FindAsync(id);
                            if (existing != null)
                            {
                                _dbContext.WaitRecords.Remove(existing);
                            }
                        }
                    }

                    // 3. Prune cancelled waits based on cancellation tokens
                    var cancelledTokens = state.CancellationHistory?.Select(h => h.Token).ToHashSet() ?? new HashSet<string>();
                    if (cancelledTokens.Count > 0)
                    {
                        var activeWaitsInDb = await _dbContext.WaitRecords
                            .Where(w => w.WorkflowInstanceId == state.Id)
                            .ToListAsync();

                        foreach (var waitRec in activeWaitsInDb)
                        {
                            if (completedWaitIds != null && completedWaitIds.Contains(waitRec.Id))
                            {
                                continue;
                            }

                            if (!string.IsNullOrEmpty(waitRec.CancelTokens))
                            {
                                var tokens = waitRec.CancelTokens.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                if (tokens.Any(t => cancelledTokens.Contains(t.Trim())))
                                {
                                    _dbContext.WaitRecords.Remove(waitRec);
                                }
                            }
                        }
                    }

                    // 4. Flatten and insert new waits
                    if (newWaits != null)
                    {
                        var flattenedRecords = new List<DbWaitRecord>();
                        foreach (var wait in newWaits)
                        {
                            FlattenAndCollectWaits(wait, null, state.Id, flattenedRecords);
                        }

                        foreach (var record in flattenedRecords)
                        {
                            var existing = await _dbContext.WaitRecords.FindAsync(record.Id);
                            if (existing != null)
                            {
                                // If it already exists, update it
                                existing.Status = record.Status;
                                existing.StateAfterWait = record.StateAfterWait;
                                existing.StateKey = record.StateKey;
                                existing.ParentWaitId = record.ParentWaitId;
                                existing.WaitName = record.WaitName;
                                existing.WaitType = record.WaitType;
                                existing.SignalPath = record.SignalPath;
                                existing.CommandWaitId = record.CommandWaitId;
                                existing.DtoJson = record.DtoJson;
                                existing.DtoType = record.DtoType;
                                existing.CancelTokens = record.CancelTokens;
                                _dbContext.WaitRecords.Update(existing);
                            }
                            else
                            {
                                _dbContext.WaitRecords.Add(record);
                            }
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

        private void FlattenAndCollectWaits(
            WaitInfrastructureDto wait,
            Guid? parentWaitId,
            Guid workflowInstanceId,
            List<DbWaitRecord> resultList)
        {
            if (wait == null) return;

            wait.ParentWaitId = parentWaitId;

            var originalChildWaits = wait.ChildWaits;
            wait.ChildWaits = new List<WaitInfrastructureDto>();

            var record = new DbWaitRecord
            {
                Id = wait.Id,
                WorkflowInstanceId = workflowInstanceId,
                Status = (int)wait.Status,
                StateAfterWait = wait.StateAfterWait,
                StateKey = wait.StateKey,
                ParentWaitId = parentWaitId,
                WaitName = wait.WaitName,
                WaitType = (int)wait.WaitType,
                DtoJson = _serializer.Serialize(wait, SerializationScope.Standard).ToString(),
                DtoType = wait.GetType().AssemblyQualifiedName ?? wait.GetType().FullName,
                CancelTokens = wait.CancelTokens != null ? string.Join(",", wait.CancelTokens) : string.Empty
            };

            wait.ChildWaits = originalChildWaits;

            if (wait is SignalWaitDto signalWait)
            {
                record.SignalPath = signalWait.SignalIdentifier;
            }
            else if (wait is TimeWaitDto timeWait)
            {
                record.SignalPath = timeWait.UniqueMatchId;
            }
            else if (wait is CommandWaitDto commandWait)
            {
                record.CommandWaitId = commandWait.Id;
            }

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
            var dbState = await _dbContext.WorkflowStates.FindAsync(instanceId);
            if (dbState == null) return null;

            var stateObject = _serializer.Deserialize<WorkflowStateObject>(dbState.StateObjectJson, SerializationScope.CompilerGeneratedClass);
            var cancellationHistory = _serializer.Deserialize<List<CancellationHistoryEntry>>(dbState.CancellationHistoryJson, SerializationScope.Standard);

            var dbWaits = await _dbContext.WaitRecords
                .Where(w => w.WorkflowInstanceId == instanceId)
                .ToListAsync();

            var dtoList = new List<WaitInfrastructureDto>();
            foreach (var dbWait in dbWaits)
            {
                var type = Type.GetType(dbWait.DtoType);
                if (type == null)
                {
                    throw new InvalidOperationException($"Could not load type '{dbWait.DtoType}' for wait ID '{dbWait.Id}'.");
                }

                var dto = (WaitInfrastructureDto)_serializer.Deserialize(dbWait.DtoJson, type, SerializationScope.Standard);
                if (dto != null)
                {
                    // Sync up properties that might be stored in columns
                    dto.Id = dbWait.Id;
                    dto.Status = (WaitStatus)dbWait.Status;
                    dto.ParentWaitId = dbWait.ParentWaitId;
                    dto.WaitName = dbWait.WaitName;
                    dto.WaitType = (WaitType)dbWait.WaitType;
                    dto.StateAfterWait = dbWait.StateAfterWait;
                    dto.StateKey = dbWait.StateKey;
                    dto.CancelTokens = !string.IsNullOrEmpty(dbWait.CancelTokens)
                        ? new System.Collections.Generic.HashSet<string>(dbWait.CancelTokens.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()))
                        : new System.Collections.Generic.HashSet<string>();
                    dto.ChildWaits = new List<WaitInfrastructureDto>(); // Reset so we reconstruct cleanly
                    dtoList.Add(dto);
                }
            }

            // Reconstruct hierarchy
            var rootWaits = new List<WaitInfrastructureDto>();
            var waitMap = dtoList.ToDictionary(w => w.Id);

            foreach (var dto in dtoList)
            {
                if (dto.ParentWaitId.HasValue && waitMap.TryGetValue(dto.ParentWaitId.Value, out var parent))
                {
                    parent.ChildWaits.Add(dto);
                }
                else
                {
                    rootWaits.Add(dto);
                }
            }

            return new WorkflowStateDto
            {
                Id = dbState.Id,
                Created = dbState.Created,
                Status = (WorkflowInstanceStatus)dbState.Status,
                WorkflowType = dbState.WorkflowType,
                StateObject = stateObject,
                Waits = rootWaits,
                CancellationHistory = cancellationHistory
            };
        }

        public async Task<List<Guid>> FindInstancesWaitingForSignalAsync(string signalPath)
        {
            return await _dbContext.WaitRecords
                .Where(w => w.SignalPath == signalPath && w.Status == (int)WaitStatus.Waiting)
                .Select(w => w.WorkflowInstanceId)
                .Distinct()
                .ToListAsync();
        }

        public async Task<Guid> GetInstanceByCommandWaitIdAsync(Guid commandWaitId)
        {
            var record = await _dbContext.WaitRecords
                .FirstOrDefaultAsync(w => w.CommandWaitId == commandWaitId);
            return record?.WorkflowInstanceId ?? Guid.Empty;
        }
    }
}
