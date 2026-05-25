using System;

namespace Workflows.Storage.EntityFrameworkCore
{
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
