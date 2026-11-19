using System;

namespace Workflows.Abstraction.DTOs
{
    /// <summary>
    /// Message dispatched by the Orchestrator to an external command-handler service
    /// when a workflow reaches a Deferred command wait.
    /// The receiving service is expected to execute the command and post a
    /// <see cref="CommandResultDto"/> back to the Orchestrator.
    /// </summary>
    public class CommandDispatchNotification
    {
        /// <summary>
        /// Correlates the result back to the persisted wait record.
        /// Must be forwarded unchanged in the <see cref="CommandResultDto"/>.
        /// </summary>
        public string CommandWaitId { get; set; } = string.Empty;

        /// <summary>
        /// Key used to route the notification to the correct
        /// <c>ICommandExecutor&lt;TCommand, TResult&gt;</c> on the client side.
        /// Matches the key supplied during workflow command registration.
        /// </summary>
        public string HandlerKey { get; set; }

        /// <summary>
        /// The deserialized command payload. The worker deserializes this to
        /// the registered <c>TCommand</c> type before calling <c>ExecuteAsync</c>.
        /// </summary>
        public object CommandData { get; set; }

        /// <summary>UTC timestamp set by the Orchestrator before dispatch.</summary>
        public DateTime DispatchedAt { get; set; }
    }
}
