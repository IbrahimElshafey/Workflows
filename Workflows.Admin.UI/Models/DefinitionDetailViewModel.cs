namespace Workflows.Admin.UI.Models
{
    /// <summary>
    /// View model for the workflow definition detail page.
    /// </summary>
    public class DefinitionDetailViewModel
    {
        public string WorkflowName { get; set; } = string.Empty;
        public int Version { get; set; }
        public string WorkflowTypeName { get; set; } = string.Empty;
        public string WorkflowTypeSchema { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }
        public int InstanceCount { get; set; }

        public TopologyGraphViewModel Topology { get; set; } = new();
    }

    public class TopologyGraphViewModel
    {
        public List<TopologyNodeViewModel> Nodes { get; set; } = new();
        public List<TopologyEdgeViewModel> Edges { get; set; } = new();
    }

    public class TopologyNodeViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // Start, SignalWait, TimeWait, CommandWait, SubWorkflowWait, CompensationWait, End
        public string? SignalIdentifier { get; set; }
        public string? HandlerKey { get; set; }
        public string? UniqueMatchId { get; set; }
        public string? CompensationToken { get; set; }
    }

    public class TopologyEdgeViewModel
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string? Label { get; set; }
    }
}
