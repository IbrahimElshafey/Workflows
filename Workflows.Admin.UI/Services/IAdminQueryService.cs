using Workflows.Admin.UI.Models;

namespace Workflows.Admin.UI.Services
{
    /// <summary>
    /// Read-only admin query service for the Workflows Admin UI.
    /// </summary>
    public interface IAdminQueryService
    {
        Task<DashboardViewModel> GetDashboardMetricsAsync(CancellationToken ct = default);
        Task<InstanceListViewModel> QueryInstancesAsync(InstanceFilterViewModel filter, int defaultPageSize, int maxPageSize, CancellationToken ct = default);
        Task<InstanceDetailViewModel?> GetInstanceDetailAsync(Guid instanceId, bool canExecuteActions, CancellationToken ct = default);
        Task<DefinitionListViewModel> GetDefinitionsAsync(CancellationToken ct = default);
        Task<DefinitionDetailViewModel?> GetDefinitionDetailAsync(string workflowName, int version, CancellationToken ct = default);
        Task<TraceViewModel?> GetExecutionTraceAsync(Guid instanceId, CancellationToken ct = default);
    }
}
