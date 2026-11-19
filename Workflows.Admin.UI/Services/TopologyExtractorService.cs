using Microsoft.EntityFrameworkCore;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Admin.UI.Models;
using Workflows.Storage.EntityFrameworkCore;

namespace Workflows.Admin.UI.Services
{
    public class TopologyExtractorService : ITopologyExtractorService
    {
        private readonly WorkflowsDbContext _dbContext;

        public TopologyExtractorService(WorkflowsDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<TopologyGraphViewModel?> ExtractTopologyAsync(string workflowName, int version, CancellationToken ct = default)
        {
            // Try to extract topology from the first-wait template instance created at registration time.
            var templateInstance = await _dbContext.WorkflowInstances
                .AsNoTracking()
                .Where(i => i.WorkflowType == workflowName && i.WorkflowVersion == version)
                .OrderBy(i => i.Created)
                .FirstOrDefaultAsync(ct);

            if (templateInstance == null || templateInstance.Waits == null || !templateInstance.Waits.Any())
            {
                return new TopologyGraphViewModel();
            }

            var graph = new TopologyGraphViewModel();
            var startNode = new TopologyNodeViewModel
            {
                Id = "start",
                Label = "Start",
                Type = "Start"
            };
            graph.Nodes.Add(startNode);

            var previousNodeId = "start";
            foreach (var wait in templateInstance.Waits)
            {
                var (nodes, edges, lastNodeId) = BuildTopologyRecursive(wait, previousNodeId, graph.Nodes.Select(n => n.Id).ToHashSet());
                graph.Nodes.AddRange(nodes);
                graph.Edges.AddRange(edges);
                previousNodeId = lastNodeId;
            }

            var endNode = new TopologyNodeViewModel
            {
                Id = "end",
                Label = "End",
                Type = "End"
            };
            graph.Nodes.Add(endNode);
            graph.Edges.Add(new TopologyEdgeViewModel { From = previousNodeId, To = "end" });

            return graph;
        }

        private static (List<TopologyNodeViewModel> nodes, List<TopologyEdgeViewModel> edges, string lastNodeId) BuildTopologyRecursive(
            WaitInfrastructureDto wait,
            string parentNodeId,
            HashSet<string> existingIds)
        {
            var nodes = new List<TopologyNodeViewModel>();
            var edges = new List<TopologyEdgeViewModel>();

            var nodeId = existingIds.Contains(wait.Id) ? $"{wait.Id}_{Guid.NewGuid():N}" : wait.Id;
            existingIds.Add(nodeId);

            var node = MapWaitToNode(wait, nodeId);
            nodes.Add(node);
            edges.Add(new TopologyEdgeViewModel { From = parentNodeId, To = nodeId });

            var lastNodeId = nodeId;

            if (wait.ChildWaits != null && wait.ChildWaits.Any())
            {
                foreach (var child in wait.ChildWaits)
                {
                    var (childNodes, childEdges, childLastId) = BuildTopologyRecursive(child, nodeId, existingIds);
                    nodes.AddRange(childNodes);
                    edges.AddRange(childEdges);
                    lastNodeId = childLastId;
                }
            }

            if (wait is ExternalGroupWaitDto externalGroup && externalGroup.ExternalChildWaits != null)
            {
                foreach (var child in externalGroup.ExternalChildWaits)
                {
                    var (childNodes, childEdges, childLastId) = BuildTopologyRecursive(child, nodeId, existingIds);
                    nodes.AddRange(childNodes);
                    edges.AddRange(childEdges);
                    lastNodeId = childLastId;
                }
            }

            return (nodes, edges, lastNodeId);
        }

        private static TopologyNodeViewModel MapWaitToNode(WaitInfrastructureDto wait, string nodeId)
        {
            var node = new TopologyNodeViewModel
            {
                Id = nodeId,
                Label = string.IsNullOrEmpty(wait.WaitName) ? wait.WaitType.ToString() : wait.WaitName,
                Type = wait.WaitType.ToString()
            };

            switch (wait)
            {
                case SignalWaitDto signalWait:
                    node.SignalIdentifier = signalWait.SignalIdentifier;
                    break;
                case TimeWaitDto timeWait:
                    node.UniqueMatchId = timeWait.UniqueMatchId;
                    break;
                case CommandWaitDto commandWait:
                    node.HandlerKey = commandWait.HandlerKey;
                    break;
                case CompensationWaitDto compensationWait:
                    node.CompensationToken = compensationWait.Token;
                    break;
            }

            return node;
        }
    }
}
