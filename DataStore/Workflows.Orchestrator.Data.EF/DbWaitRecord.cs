using System;

namespace Workflows.Orchestrator.Data.EF
{
    public class DbWaitRecord
    {
        public Guid Id { get; set; }
        public Guid WorkflowInstanceId { get; set; }
        public int Status { get; set; } // Map from WaitStatus
        public int StateAfterWait { get; set; }
        public Guid StateKey { get; set; }
        public Guid? ParentWaitId { get; set; }
        public string WaitName { get; set; }
        public int WaitType { get; set; } // Map from WaitType
        public string? SignalPath { get; set; } // Stores SignalWaitDto.SignalIdentifier or TimeWaitDto.UniqueMatchId
        public Guid? CommandWaitId { get; set; } // Stores CommandWaitDto.Id
        public string DtoJson { get; set; }
        public string DtoType { get; set; }
        public string CancelTokens { get; set; } // Comma-separated list of cancel tokens
    }
}
