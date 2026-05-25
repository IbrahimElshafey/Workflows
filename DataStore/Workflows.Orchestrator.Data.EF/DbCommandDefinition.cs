using System;

namespace Workflows.Orchestrator.Data.EF
{
    public class DbCommandDefinition
    {
        public string CommandName { get; set; }
        public string PayloadTypeName { get; set; }
        public string PayloadSchema { get; set; }
        public string ResultTypeName { get; set; }
        public string ResultSchema { get; set; }
        public TimeSpan DefaultTimeout { get; set; }
        public int ExecutionMode { get; set; }
    }
}
