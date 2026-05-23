using System;
using System.Collections.Generic;
using Workflows.Abstraction.Enums;

namespace Workflows.TestShell
{
    public class ExecutionLogEntry
    {
        public Guid TriggeringWaitId { get; set; }
        public List<Guid> ConsumedWaitIds { get; set; } = new();
        public List<Guid> NewWaitIds { get; set; } = new();
        public WorkflowInstanceStatus Status { get; set; }
        public string SerializedStateSnapshot { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
