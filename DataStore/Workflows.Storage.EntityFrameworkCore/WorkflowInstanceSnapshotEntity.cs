using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    /// <summary>
    /// Persisted pre-migration snapshot of a workflow instance (see
    /// <see cref="Workflows.Abstraction.Persistence.WorkflowInstanceSnapshot"/>).
    /// </summary>
    public class WorkflowInstanceSnapshotEntity
    {
        public string Id { get; set; } = string.Empty;
        public Guid WorkflowInstanceId { get; set; }
        public string WorkflowType { get; set; } = string.Empty;
        public int FromVersion { get; set; }
        public int ToVersion { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string StateJson { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}