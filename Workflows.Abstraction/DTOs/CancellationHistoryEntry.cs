using System;

namespace Workflows.Abstraction.DTOs
{
    /// <summary>
    /// Represents a single cancellation event in the workflow's lifetime.
    /// Used for audit trail and to determine which waits should be cancelled.
    /// </summary>
    public class CancellationHistoryEntry
    {
        /// <summary>
        /// The cancellation token that was triggered.
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// UTC timestamp when the cancellation was triggered.
        /// </summary>
        public DateTime CancelledAt { get; set; }

        /// <summary>
        /// Optional reason or context for the cancellation.
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// Optional identifier of the replacement V2 workflow instance if respawned.
        /// </summary>
        public string? ReplacementWorkflowInstanceId { get; set; }

        /// <summary>
        /// Optional audit link pointing to the newly spawned V2 instance.
        /// </summary>
        public string? AuditLink { get; set; }
    }
}
