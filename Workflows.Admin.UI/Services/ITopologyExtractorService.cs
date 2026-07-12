using Workflows.Admin.UI.Models;

namespace Workflows.Admin.UI.Services
{
    /// <summary>
    /// Extracts a directed acyclic graph (DAG) representation from a workflow definition.
    /// </summary>
    public interface ITopologyExtractorService
    {
        Task<TopologyGraphViewModel?> ExtractTopologyAsync(string workflowName, int version, CancellationToken ct = default);
    }
}
