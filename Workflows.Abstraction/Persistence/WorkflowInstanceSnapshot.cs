using System;

namespace Workflows.Abstraction.Persistence
{
    /// <summary>
    /// Point-in-time copy of an instance's persisted state, written before a version migration mutates it.
    /// A failed migration leaves the instance on its original version (status
    /// <c>WorkflowInstanceStatus.MigrationFailed</c>) and this snapshot is the state the instance had when
    /// the attempt started.
    /// </summary>
    public class WorkflowInstanceSnapshot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public Guid InstanceId { get; set; }

        public string WorkflowType { get; set; } = string.Empty;

        /// <summary>Version the instance was on before the migration.</summary>
        public int FromVersion { get; set; }

        /// <summary>Version the migration was attempting to reach.</summary>
        public int ToVersion { get; set; }

        /// <summary>Why the snapshot was taken (migration id, operator action, ...).</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>Serialized <c>WorkflowStateDto</c> as it existed before the migration.</summary>
        public string StateJson { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}