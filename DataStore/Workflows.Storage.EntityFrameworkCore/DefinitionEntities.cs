using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class WorkflowDefinitionEntity : IEntity
    {
        public string WorkflowName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string WorkflowTypeName { get; set; } = string.Empty;
        public string WorkflowTypeSchema { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }
        public DateTime Created { get; set; }
    }

    public class SignalDefinitionEntity : IEntity
    {
        public string SignalIdentifier { get; set; } = string.Empty;
        public string PayloadTypeName { get; set; } = string.Empty;
        public string PayloadSchema { get; set; } = string.Empty;
        public DateTime Created { get; set; }
    }

    public class CommandDefinitionEntity : IEntity
    {
        public string CommandName { get; set; } = string.Empty;
        public string PayloadTypeName { get; set; } = string.Empty;
        public string PayloadSchema { get; set; } = string.Empty;
        public string ResultTypeName { get; set; } = string.Empty;
        public string ResultSchema { get; set; } = string.Empty;
        public TimeSpan DefaultTimeout { get; set; }
        public int ExecutionMode { get; set; }
        public DateTime Created { get; set; }
    }
}
