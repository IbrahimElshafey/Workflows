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
    }
}
