using System;
using System.Threading.Tasks;

namespace Workflows.Abstraction.Persistence
{
    /// <summary>
    /// Represents a message that has been routed to the Dead Letter Queue because
    /// a workflow instance exceeded its consecutive-failure threshold.
    /// </summary>
    public class DlqMessage
    {
        /// <summary>Workflow instance that exceeded the failure threshold.</summary>
        public Guid InstanceId { get; set; }

        /// <summary>Fully-qualified workflow type name.</summary>
        public string WorkflowType { get; set; } = string.Empty;

        /// <summary>Failure count at the time of DLQ routing.</summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// Serialized JSON of the triggering payload (signal data or command result).
        /// Null when unavailable (e.g., a startup failure with no payload).
        /// </summary>
        public string? PayloadJson { get; set; }

        /// <summary>Last unhandled exception message.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>UTC timestamp when the DLQ routing occurred.</summary>
        public DateTime FailedAt { get; set; }
    }

    /// <summary>
    /// Publishes a <see cref="DlqMessage"/> to the Dead Letter Queue ("workflows-dlq").
    /// Register a concrete implementation to forward entries to a real message bus;
    /// by default a no-op implementation is registered so no infrastructure is required.
    /// </summary>
    public interface IDlqPublisher
    {
        Task PublishAsync(DlqMessage message);
    }
}
