using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Admin.UI.Models;
using Workflows.Storage.EntityFrameworkCore;

namespace Workflows.Admin.UI.Services
{
    public class AdminQueryService : IAdminQueryService
    {
        private readonly WorkflowsDbContext _dbContext;
        private readonly IMetricsAggregatorService _metricsAggregator;
        private readonly ITopologyExtractorService _topologyExtractor;
        private readonly ITraceAggregatorService _traceAggregator;

        public AdminQueryService(
            WorkflowsDbContext dbContext,
            IMetricsAggregatorService metricsAggregator,
            ITopologyExtractorService topologyExtractor,
            ITraceAggregatorService traceAggregator)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _metricsAggregator = metricsAggregator ?? throw new ArgumentNullException(nameof(metricsAggregator));
            _topologyExtractor = topologyExtractor ?? throw new ArgumentNullException(nameof(topologyExtractor));
            _traceAggregator = traceAggregator ?? throw new ArgumentNullException(nameof(traceAggregator));
        }

        public Task<DashboardViewModel> GetDashboardMetricsAsync(CancellationToken ct = default)
            => _metricsAggregator.GetDashboardMetricsAsync(ct);

        public async Task<InstanceListViewModel> QueryInstancesAsync(
            InstanceFilterViewModel filter,
            int defaultPageSize,
            int maxPageSize,
            CancellationToken ct = default)
        {
            var pageSize = Math.Min(filter.PageSize > 0 ? filter.PageSize : defaultPageSize, maxPageSize);
            var page = Math.Max(filter.Page, 1);

            var query = _dbContext.WorkflowInstances.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(filter.InstanceId) && Guid.TryParse(filter.InstanceId, out var instanceId))
            {
                query = query.Where(i => i.Id == instanceId);
            }

            if (!string.IsNullOrWhiteSpace(filter.WorkflowType))
            {
                query = query.Where(i => i.WorkflowType == filter.WorkflowType);
            }

            if (filter.Status.HasValue)
            {
                var statusValue = (int)filter.Status.Value;
                query = query.Where(i => i.Status == statusValue);
            }

            if (filter.CreatedAfter.HasValue)
            {
                query = query.Where(i => i.Created >= filter.CreatedAfter.Value);
            }

            if (filter.CreatedBefore.HasValue)
            {
                query = query.Where(i => i.Created <= filter.CreatedBefore.Value);
            }

            if (filter.HasError.HasValue)
            {
                if (filter.HasError.Value)
                    query = query.Where(i => !string.IsNullOrEmpty(i.ErrorMessage));
                else
                    query = query.Where(i => string.IsNullOrEmpty(i.ErrorMessage));
            }

            var totalCount = await query.CountAsync(ct);

            var items = await query
                .OrderByDescending(i => i.Created)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(i => new InstanceSummaryViewModel
                {
                    Id = i.Id,
                    WorkflowType = i.WorkflowType,
                    WorkflowVersion = i.WorkflowVersion,
                    Status = (WorkflowInstanceStatus)i.Status,
                    Created = i.Created,
                    Modified = i.Modified,
                    CompletedAt = i.CompletedAt,
                    LockedBy = i.LockedBy,
                    LockExpiresAt = i.LockExpiresAt,
                    ActiveWaitsCount = i.ActiveWaitCount,
                    ErrorMessage = i.ErrorMessage,
                    LastAdvanceReason = i.LastAdvanceReason
                })
                .ToListAsync(ct);

            return new InstanceListViewModel
            {
                Filter = filter,
                PagedResult = new PagedResult<InstanceSummaryViewModel>
                {
                    Items = items,
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount
                }
            };
        }

        public async Task<InstanceDetailViewModel?> GetInstanceDetailAsync(Guid instanceId, bool canExecuteActions, CancellationToken ct = default)
        {
            var instance = await _dbContext.WorkflowInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == instanceId, ct);

            if (instance == null) return null;

            var stateObject = instance.StateObject;
            var variablesJson = stateObject?.Instance != null
                ? JsonConvert.SerializeObject(stateObject.Instance, Formatting.Indented)
                : "{}";

            var localsJson = stateObject?.Locals != null
                ? JsonConvert.SerializeObject(stateObject.Locals, Formatting.Indented)
                : "{}";

            var waitTree = BuildWaitTree(instance.Waits);
            var activeSignalWaits = CollectActiveSignalWaits(instance.Waits);

            return new InstanceDetailViewModel
            {
                Id = instance.Id,
                WorkflowType = instance.WorkflowType,
                WorkflowVersion = instance.WorkflowVersion,
                Status = (WorkflowInstanceStatus)instance.Status,
                Created = instance.Created,
                Modified = instance.Modified,
                CompletedAt = instance.CompletedAt,
                LockedBy = instance.LockedBy,
                LockedAt = instance.LockedAt,
                LockExpiresAt = instance.LockExpiresAt,
                ErrorMessage = instance.ErrorMessage,
                LastAdvanceReason = instance.LastAdvanceReason,
                VariablesJson = variablesJson,
                LocalsJson = localsJson,
                WaitTree = waitTree,
                CancellationHistory = instance.CancellationHistory ?? new List<Workflows.Abstraction.DTOs.CancellationHistoryEntry>(),
                CanExecuteActions = canExecuteActions,
                ActiveSignalWaits = activeSignalWaits
            };
        }

        public async Task<DefinitionListViewModel> GetDefinitionsAsync(CancellationToken ct = default)
        {
            var definitions = await _dbContext.WorkflowDefinitions
                .AsNoTracking()
                .OrderBy(d => d.WorkflowName)
                .ThenBy(d => d.Version)
                .ToListAsync(ct);

            var instanceCounts = await _dbContext.WorkflowInstances
                .AsNoTracking()
                .GroupBy(i => new { i.WorkflowType, i.WorkflowVersion })
                .Select(g => new { g.Key.WorkflowType, g.Key.WorkflowVersion, Count = g.Count() })
                .ToListAsync(ct);

            var groups = definitions
                .GroupBy(d => d.WorkflowName)
                .Select(g => new DefinitionGroupViewModel
                {
                    WorkflowName = g.Key,
                    Versions = g.Select(d => new DefinitionVersionViewModel
                    {
                        WorkflowName = d.WorkflowName,
                        Version = d.Version,
                        WorkflowTypeName = d.WorkflowTypeName,
                        RegisteredAt = d.RegisteredAt,
                        InstanceCount = instanceCounts
                            .FirstOrDefault(c => c.WorkflowType == d.WorkflowName && c.WorkflowVersion == d.Version)?.Count ?? 0
                    }).ToList(),
                    TotalInstances = instanceCounts
                        .Where(c => c.WorkflowType == g.Key)
                        .Sum(c => c.Count)
                })
                .ToList();

            return new DefinitionListViewModel { Groups = groups };
        }

        public async Task<DefinitionDetailViewModel?> GetDefinitionDetailAsync(string workflowName, int version, CancellationToken ct = default)
        {
            var definition = await _dbContext.WorkflowDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.WorkflowName == workflowName && d.Version == version, ct);

            if (definition == null) return null;

            var instanceCount = await _dbContext.WorkflowInstances
                .AsNoTracking()
                .CountAsync(i => i.WorkflowType == workflowName && i.WorkflowVersion == version, ct);

            var topology = await _topologyExtractor.ExtractTopologyAsync(workflowName, version, ct);

            return new DefinitionDetailViewModel
            {
                WorkflowName = definition.WorkflowName,
                Version = definition.Version,
                WorkflowTypeName = definition.WorkflowTypeName,
                WorkflowTypeSchema = definition.WorkflowTypeSchema,
                RegisteredAt = definition.RegisteredAt,
                InstanceCount = instanceCount,
                Topology = topology ?? new TopologyGraphViewModel()
            };
        }

        public Task<TraceViewModel?> GetExecutionTraceAsync(Guid instanceId, CancellationToken ct = default)
            => _traceAggregator.GetExecutionTraceAsync(instanceId, ct);

        private static int CountActiveWaits(List<WaitInfrastructureDto>? waits)
        {
            if (waits == null) return 0;
            var count = 0;
            foreach (var wait in waits)
            {
                if (wait.Status == WaitStatus.Waiting) count++;
                count += CountActiveWaits(wait.ChildWaits);
                if (wait is ExternalGroupWaitDto externalGroup)
                {
                    count += CountActiveWaits(externalGroup.ExternalChildWaits);
                }
            }
            return count;
        }

        private static List<WaitTreeNodeViewModel> BuildWaitTree(List<WaitInfrastructureDto>? waits)
        {
            if (waits == null) return new List<WaitTreeNodeViewModel>();
            return waits.Select(MapWait).ToList();
        }

        private static WaitTreeNodeViewModel MapWait(WaitInfrastructureDto wait)
        {
            var node = new WaitTreeNodeViewModel
            {
                Id = wait.Id,
                WaitName = wait.WaitName,
                WaitType = wait.WaitType.ToString(),
                Status = wait.Status.ToString(),
                CallerName = wait.CallerName,
                InCodeLine = wait.InCodeLine,
                Created = wait.Created,
                ChildWaits = BuildWaitTree(wait.ChildWaits),
                CancelTokens = wait.CancelTokens?.ToList() ?? new List<string>()
            };

            switch (wait)
            {
                case SignalWaitDto signalWait:
                    node.SignalIdentifier = signalWait.SignalIdentifier;
                    node.ExactMatchFilter = signalWait.ExactMatchPart;
                    node.IsFirstWait = signalWait.IsFirstWait;
                    break;
                case TimeWaitDto timeWait:
                    node.ExecutionTime = timeWait.ExecutionTime;
                    node.UniqueMatchId = timeWait.UniqueMatchId;
                    break;
                case CommandWaitDto commandWait:
                    node.HandlerKey = commandWait.HandlerKey;
                    node.CommandWaitId = commandWait.Id;
                    break;
                case CompensationWaitDto compensationWait:
                    node.CompensationToken = compensationWait.Token;
                    break;
                case ExternalGroupWaitDto externalGroup:
                    node.ChildWaits.AddRange(BuildWaitTree(externalGroup.ExternalChildWaits));
                    break;
            }

            return node;
        }

        private static List<SignalWaitOption> CollectActiveSignalWaits(List<WaitInfrastructureDto>? waits)
        {
            var result = new List<SignalWaitOption>();
            if (waits == null) return result;

            foreach (var wait in waits)
            {
                if (wait is SignalWaitDto signalWait && wait.Status == WaitStatus.Waiting)
                {
                    result.Add(new SignalWaitOption
                    {
                        WaitId = wait.Id,
                        SignalIdentifier = signalWait.SignalIdentifier,
                        ExactMatchFilter = signalWait.ExactMatchPart
                    });
                }

                result.AddRange(CollectActiveSignalWaits(wait.ChildWaits));

                if (wait is ExternalGroupWaitDto externalGroup)
                {
                    result.AddRange(CollectActiveSignalWaits(externalGroup.ExternalChildWaits));
                }
            }

            return result;
        }
    }
}
