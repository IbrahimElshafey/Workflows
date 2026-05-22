using System;

namespace Workflows.Orchestrator.Data.EF
{
    public class DbWorkflowState
    {
        public Guid Id { get; set; }
        public DateTime Created { get; set; }
        public int Status { get; set; } // Map from WorkflowInstanceStatus
        public string WorkflowType { get; set; }
        public string StateObjectJson { get; set; }
        public string CancellationHistoryJson { get; set; }
    }
}
