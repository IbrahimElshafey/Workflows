using Microsoft.EntityFrameworkCore;
using Workflows.Abstraction.Enums;
using Workflows.Admin.UI.Models;
using Workflows.Storage.EntityFrameworkCore;

namespace Workflows.Admin.UI.Services
{
    public class MetricsAggregatorService : IMetricsAggregatorService
    {
        private readonly WorkflowsDbContext _dbContext;

        public MetricsAggregatorService(WorkflowsDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<DashboardViewModel> GetDashboardMetricsAsync(CancellationToken ct = default)
        {
            var model = new DashboardViewModel();

            var query = _dbContext.WorkflowInstances.AsNoTracking();

            model.TotalInstances = await query.CountAsync(ct);
            model.RunningInstances = await query.CountAsync(i => i.Status == (int)WorkflowInstanceStatus.Running, ct);
            model.SuspendedInstances = await query.CountAsync(i => i.Status == (int)WorkflowInstanceStatus.InProgress, ct);
            model.CompletedInstances = await query.CountAsync(i => i.Status == (int)WorkflowInstanceStatus.Completed, ct);
            model.FaultedInstances = await query.CountAsync(i => i.Status == (int)WorkflowInstanceStatus.InError, ct);
            model.CanceledInstances = await query.CountAsync(i => i.Status == (int)WorkflowInstanceStatus.Canceled, ct);

            model.ActiveSignalWaits = await _dbContext.SignalWaits
                .AsNoTracking()
                .CountAsync(w => w.Status == (int)Workflows.Abstraction.Enums.WaitStatus.Waiting, ct);

            model.ActiveCommandWaits = await _dbContext.CommandWaits
                .AsNoTracking()
                .CountAsync(w => w.Status == (int)Workflows.Abstraction.Enums.WaitStatus.Waiting, ct);

            model.ActiveTimeWaits = await _dbContext.TimeWaits
                .AsNoTracking()
                .CountAsync(w => w.Status == (int)Workflows.Abstraction.Enums.WaitStatus.Waiting, ct);

            model.ActiveCompensationWaits = await _dbContext.CompensationWaits
                .AsNoTracking()
                .CountAsync(w => w.Status == (int)Workflows.Abstraction.Enums.WaitStatus.Waiting, ct);

            model.FailureRate = model.TotalInstances == 0
                ? 0
                : Math.Round((double)model.FaultedInstances / model.TotalInstances * 100, 2);

            // Average execution latency: use CompletedAt - Created for completed instances
            var completedLatencies = await query
                .Where(i => i.Status == (int)WorkflowInstanceStatus.Completed && i.CompletedAt.HasValue)
                .Select(i => (i.CompletedAt.Value - i.Created).TotalMilliseconds)
                .ToListAsync(ct);

            model.AverageExecutionLatencyMs = completedLatencies.Count == 0
                ? 0
                : Math.Round(completedLatencies.Average(), 2);

            model.InstancesByWorkflowType = await query
                .GroupBy(i => i.WorkflowType)
                .Select(g => new WorkflowTypeCount { WorkflowType = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync(ct);

            model.DailyInstanceCounts = await BuildDailyCountsAsync(ct);
            model.Alerts = await BuildAlertsAsync(ct);

            return model;
        }

        private async Task<List<DailyInstanceCount>> BuildDailyCountsAsync(CancellationToken ct)
        {
            var last30Days = Enumerable.Range(0, 30)
                .Select(offset => DateTime.UtcNow.Date.AddDays(-offset))
                .Reverse()
                .ToList();

            var startedCounts = await _dbContext.WorkflowInstances
                .AsNoTracking()
                .Where(i => i.Created >= last30Days.First())
                .GroupBy(i => i.Created.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Date, x => x.Count, ct);

            var completedCounts = await _dbContext.WorkflowInstances
                .AsNoTracking()
                .Where(i => i.Status == (int)WorkflowInstanceStatus.Completed && i.CompletedAt.HasValue && i.CompletedAt.Value >= last30Days.First())
                .GroupBy(i => i.CompletedAt.Value.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Date, x => x.Count, ct);

            var faultedCounts = await _dbContext.WorkflowInstances
                .AsNoTracking()
                .Where(i => i.Status == (int)WorkflowInstanceStatus.InError && i.CompletedAt.HasValue && i.CompletedAt.Value >= last30Days.First())
                .GroupBy(i => i.CompletedAt.Value.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Date, x => x.Count, ct);

            return last30Days.Select(date => new DailyInstanceCount
            {
                Date = date,
                Started = startedCounts.GetValueOrDefault(date),
                Completed = completedCounts.GetValueOrDefault(date),
                Faulted = faultedCounts.GetValueOrDefault(date)
            }).ToList();
        }

        private async Task<List<AlertViewModel>> BuildAlertsAsync(CancellationToken ct)
        {
            var alerts = new List<AlertViewModel>();

            // Stuck instances: Running but lock expired or no recent activity
            var stuckThreshold = DateTime.UtcNow.AddHours(-1);
            var stuckInstances = await _dbContext.WorkflowInstances
                .AsNoTracking()
                .Where(i => i.Status == (int)WorkflowInstanceStatus.Running
                            && (!i.LockExpiresAt.HasValue || i.LockExpiresAt.Value < DateTime.UtcNow)
                            && (!i.Modified.HasValue || i.Modified.Value < stuckThreshold))
                .OrderBy(i => i.Modified)
                .Take(5)
                .Select(i => new { i.Id })
                .ToListAsync(ct);

            foreach (var inst in stuckInstances)
            {
                alerts.Add(new AlertViewModel
                {
                    Severity = AlertSeverity.Warning,
                    Title = "Potentially Stuck Instance",
                    Message = $"Instance {inst.Id} is Running but has no valid lock or recent activity.",
                    InstanceId = inst.Id.ToString(),
                    DetectedAt = DateTime.UtcNow
                });
            }

            // Faulted instances in last 24 hours
            var recentFaultCount = await _dbContext.WorkflowInstances
                .AsNoTracking()
                .CountAsync(i => i.Status == (int)WorkflowInstanceStatus.InError
                                 && i.CompletedAt.HasValue
                                 && i.CompletedAt.Value > DateTime.UtcNow.AddDays(-1), ct);

            if (recentFaultCount > 0)
            {
                alerts.Add(new AlertViewModel
                {
                    Severity = AlertSeverity.Critical,
                    Title = "Recent Faulted Instances",
                    Message = $"{recentFaultCount} instance(s) faulted in the last 24 hours.",
                    DetectedAt = DateTime.UtcNow
                });
            }

            return alerts;
        }
    }
}
