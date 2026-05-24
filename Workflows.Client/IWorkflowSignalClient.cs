using System.Threading;
using System.Threading.Tasks;

namespace Workflows.Client
{
    /// <summary>
    /// Sends named signal notifications to the workflow engine.
    /// Intended to be used by any external service that needs to notify
    /// running workflows of an external event (webhook, message bus event, etc.).
    /// Uses <c>IMessageDispatcher</c> under the hood and is therefore
    /// transport-agnostic — the actual delivery mechanism is determined by
    /// the <c>TransportRoutingBuilder</c> configuration registered in DI.
    /// </summary>
    public interface IWorkflowSignalClient
    {
        /// <summary>
        /// Sends a signal carrying an untyped payload to the workflow engine.
        /// </summary>
        /// <param name="signalIdentifier">
        /// The unique identifier of the signal as registered in the workflow definition
        /// (e.g., <c>"Payments.Completed"</c>).
        /// </param>
        /// <param name="data">The signal payload. May be any serializable object.</param>
        /// <param name="cancellationToken">Token to cancel the dispatch operation.</param>
        Task SendSignalAsync(string signalIdentifier, object data,
            CancellationToken cancellationToken = default);
    }
}
