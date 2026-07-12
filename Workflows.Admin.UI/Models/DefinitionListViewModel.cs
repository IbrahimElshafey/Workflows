namespace Workflows.Admin.UI.Models
{
    /// <summary>
    /// View model for the workflow definitions list page.
    /// </summary>
    public class DefinitionListViewModel
    {
        public List<DefinitionGroupViewModel> Groups { get; set; } = new();
    }

    public class DefinitionGroupViewModel
    {
        public string WorkflowName { get; set; } = string.Empty;
        public List<DefinitionVersionViewModel> Versions { get; set; } = new();
        public int TotalInstances { get; set; }
    }

    public class DefinitionVersionViewModel
    {
        public string WorkflowName { get; set; } = string.Empty;
        public int Version { get; set; }
        public string WorkflowTypeName { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }
        public int InstanceCount { get; set; }
    }
}
