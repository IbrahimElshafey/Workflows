using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    /// <summary>
    /// Persists a Dead Letter Queue entry for a workflow instance that exceeded its
    /// consecutive-failure threshold. Stored in the <c>DlqMessages</c> table and
    /// can be forwarded to an external broker (e.g. "workflows-dlq") by a sweeper worker.
    /// </summary>
    public class DlqMessageEntity
    {
        public long Id { get; set; }

        /// <summary>Instance that was quarantined.</summary>
        public Guid WorkflowInstanceId { get; set; }

        /// <summary>Fully-qualified workflow type name.</summary>
        public string WorkflowType { get; set; } = string.Empty;

        /// <summary>FailureCount value at the time of DLQ routing.</summary>
        public int FailureCount { get; set; }

        /// <summary>Serialized JSON payload of the signal or command result that triggered the last failure. May be null.</summary>
        public string? PayloadJson { get; set; }

        /// <summary>Last unhandled exception message.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>UTC timestamp when the entry was created.</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 0 = Pending (not yet forwarded to the external broker).
        /// 1 = Forwarded.
        /// </summary>
        public int Status { get; set; }
    }
}
