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
            IEnumerable<WaitInfrastructureDto> newWaits,
            IEnumerable<Guid> completedWaitIds)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

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
                            CancellationHistory = state.CancellationHistory ?? new()
                        };
                        _dbContext.WorkflowInstances.Add(dbInstance);
                    }
                    else
                    {
                        dbInstance.Status = (int)state.Status;
                        dbInstance.StateObject = state.StateObject ?? new();
                        dbInstance.CancellationHistory = state.CancellationHistory ?? new();
                        _dbContext.WorkflowInstances.Update(dbInstance);
                    }

                    // 2. Delete completed waits
                    if (completedWaitIds != null)
                    {
                        foreach (var id in completedWaitIds)
                        {
                            var existing = await _dbContext.WorkflowWaits.FindAsync(id);
                            if (existing != null)
                            {
                                _dbContext.WorkflowWaits.Remove(existing);
                            }
                        }
                    }

                    // 3. Prune cancelled waits based on cancellation tokens
                    var cancelledTokens = state.CancellationHistory?.Select(h => h.Token).ToHashSet() ?? new HashSet<string>();
                    if (cancelledTokens.Count > 0)
                    {
                        var activeWaitsInDb = await _dbContext.WorkflowWaits
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
                                    _dbContext.WorkflowWaits.Remove(waitRec);
                                }
                            }
                        }
                    }

                    // 4. Flatten and insert/update new waits
                    if (newWaits != null)
                    {
                        var flattenedRecords = new List<WorkflowWait>();
                        foreach (var wait in newWaits)
                        {
                            FlattenAndCollectWaits(wait, null, state.Id, flattenedRecords);
                        }

                        foreach (var record in flattenedRecords)
                        {
                            var existing = await _dbContext.WorkflowWaits
                                .IgnoreQueryFilters()
                                .FirstOrDefaultAsync(w => w.Id == record.Id);

                            if (existing != null)
                            {
                                existing.Status = record.Status;
                                existing.StateAfterWait = record.StateAfterWait;
                                existing.StateKey = record.StateKey;
                                existing.ParentWaitId = record.ParentWaitId;
                                existing.WaitName = record.WaitName;
                                existing.WaitType = record.WaitType;
                                existing.DtoJson = record.DtoJson;
                                existing.DtoType = record.DtoType;
                                existing.CancelTokens = record.CancelTokens;
                                existing.IsDeleted = record.IsDeleted;

                                if (record is SignalWait sigRecord && existing is SignalWait sigExisting)
                                {
                                    sigExisting.SignalPath = sigRecord.SignalPath;
                                }
                                else if (record is CommandWait cmdRecord && existing is CommandWait cmdExisting)
                                {
                                    cmdExisting.CommandWaitId = cmdRecord.CommandWaitId;
                                }

                                _dbContext.WorkflowWaits.Update(existing);
                            }
                            else
                            {
                                _dbContext.WorkflowWaits.Add(record);
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
            List<WorkflowWait> resultList)
        {
            if (wait == null) return;

            wait.ParentWaitId = parentWaitId;

            var originalChildWaits = wait.ChildWaits;
            wait.ChildWaits = new List<WaitInfrastructureDto>();

            WorkflowWait record;
            if (wait is SignalWaitDto signalWait)
            {
                record = new SignalWait
                {
                    SignalPath = signalWait.SignalIdentifier ?? string.Empty
                };
            }
            else if (wait is TimeWaitDto timeWait)
            {
                record = new SignalWait
                {
                    SignalPath = timeWait.UniqueMatchId ?? string.Empty
                };
            }
            else if (wait is CommandWaitDto commandWait)
            {
                record = new CommandWait
                {
                    CommandWaitId = commandWait.Id
                };
            }
            else
            {
                record = new WorkflowWait();
            }

            record.Id = wait.Id;
            record.WorkflowInstanceId = workflowInstanceId;
            record.Status = (int)wait.Status;
            record.StateAfterWait = wait.StateAfterWait;
            record.StateKey = wait.StateKey;
            record.ParentWaitId = parentWaitId;
            record.WaitName = wait.WaitName ?? string.Empty;
            record.WaitType = (int)wait.WaitType;
            record.DtoJson = _serializer.Serialize(wait, SerializationScope.Standard).ToString() ?? string.Empty;
            record.DtoType = wait.GetType().AssemblyQualifiedName ?? wait.GetType().FullName ?? string.Empty;
            record.CancelTokens = wait.CancelTokens != null ? string.Join(",", wait.CancelTokens) : string.Empty;

            wait.ChildWaits = originalChildWaits;
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

            var dbWaits = await _dbContext.WorkflowWaits
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
                    dto.Id = dbWait.Id;
                    dto.Status = (WaitStatus)dbWait.Status;
                    dto.ParentWaitId = dbWait.ParentWaitId;
                    dto.WaitName = dbWait.WaitName;
                    dto.WaitType = (WaitType)dbWait.WaitType;
                    dto.StateAfterWait = dbWait.StateAfterWait;
                    dto.StateKey = dbWait.StateKey;
                    dto.CancelTokens = !string.IsNullOrEmpty(dbWait.CancelTokens)
                        ? new HashSet<string>(dbWait.CancelTokens.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()))
                        : new HashSet<string>();
                    dto.ChildWaits = new List<WaitInfrastructureDto>();
                    dtoList.Add(dto);
                }
            }

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
                Id = dbInstance.Id,
                Created = dbInstance.Created,
                Status = (WorkflowInstanceStatus)dbInstance.Status,
                WorkflowType = dbInstance.WorkflowType,
                StateObject = dbInstance.StateObject,
                Waits = rootWaits,
                CancellationHistory = dbInstance.CancellationHistory
            };
        }

        public async Task<List<Guid>> FindInstancesWaitingForSignalAsync(string signalPath)
        {
            return await _dbContext.SignalWaits
                .Where(w => w.SignalPath == signalPath && w.Status == (int)WaitStatus.Waiting)
                .Select(w => w.WorkflowInstanceId)
                .Distinct()
                .ToListAsync();
        }

        public async Task<Guid> GetInstanceByCommandWaitIdAsync(Guid commandWaitId)
        {
            var record = await _dbContext.CommandWaits
                .FirstOrDefaultAsync(w => w.CommandWaitId == commandWaitId);
            return record?.WorkflowInstanceId ?? Guid.Empty;
        }
    }
}
