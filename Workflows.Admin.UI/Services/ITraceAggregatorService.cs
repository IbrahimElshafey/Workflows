using Workflows.Admin.UI.Models;

namespace Workflows.Admin.UI.Services
{
    /// <summary>
    /// Builds a unified execution trace timeline for a workflow instance.
    /// </summary>
    public interface ITraceAggregatorService
    {
        Task<TraceViewModel?> GetExecutionTraceAsync(Guid instanceId, CancellationToken ct = default);
    }
}
