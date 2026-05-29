using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class WorkflowDefinitionEntity : IEntity
    {
        public string WorkflowName { get; set; } = string.Empty;
        public int Version { get; set; }
        public string WorkflowTypeName { get; set; } = string.Empty;
        public string WorkflowTypeSchema { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }
        public DateTime Created { get; set; }
    }
}
