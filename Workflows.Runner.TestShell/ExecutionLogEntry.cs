using System;
using System.Collections.Generic;
using Workflows.Abstraction.Enums;

namespace Workflows.TestShell
{
    public class ExecutionLogEntry
    {
        public string TriggeringWaitId { get; set; } = string.Empty;
        public List<string> ConsumedWaitIds { get; set; } = new();
        public List<string> NewWaitIds { get; set; } = new();
        public WorkflowInstanceStatus Status { get; set; }
        public string SerializedStateSnapshot { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
