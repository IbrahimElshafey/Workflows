using Workflows.Admin.UI.Models;

namespace Workflows.Admin.UI.Services
{
    /// <summary>
    /// Aggregates dashboard metrics and alerts from the workflows database.
    /// </summary>
    public interface IMetricsAggregatorService
    {
        Task<DashboardViewModel> GetDashboardMetricsAsync(CancellationToken ct = default);
    }
}
