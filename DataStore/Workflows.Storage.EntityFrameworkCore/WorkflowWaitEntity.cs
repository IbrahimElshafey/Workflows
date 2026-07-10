using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class WorkflowWaitEntity : IEntity<string>
    {
        public string Id { get; set; } = string.Empty;
        public Guid WorkflowInstanceId { get; set; }
        public int Status { get; set; } // Map from WaitStatus
        public string? CancelTokens { get; set; } // Comma-separated cancel tokens for SQL querying

        public DateTime Created { get; set; }
    }
}
