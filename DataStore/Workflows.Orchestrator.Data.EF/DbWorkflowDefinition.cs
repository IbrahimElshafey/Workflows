using System;

namespace Workflows.Orchestrator.Data.EF
{
    public class DbWorkflowDefinition
    {
        public string WorkflowName { get; set; }
        public string Version { get; set; }
        public string WorkflowTypeName { get; set; }
        public string WorkflowTypeSchema { get; set; }
        public DateTime RegisteredAt { get; set; }
    }
}
