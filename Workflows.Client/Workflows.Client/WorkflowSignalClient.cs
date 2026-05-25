using System;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Communication.Abstraction;

namespace Workflows.Client
{
    /// <summary>
    /// Default implementation of <see cref="IWorkflowSignalClient"/>.
    /// Wraps <see cref="IMessageDispatcher"/> so the transport (HTTP, Service Bus,
    /// in-process, etc.) is determined entirely by the <c>TransportRoutingBuilder</c>
    /// registered at startup.
    /// </summary>
    public sealed class WorkflowSignalClient : IWorkflowSignalClient
    {
        private readonly IMessageDispatcher _dispatcher;

        public WorkflowSignalClient(IMessageDispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <inheritdoc/>
        public Task SendSignalAsync(string signalIdentifier, object data,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(signalIdentifier))
                throw new ArgumentNullException(nameof(signalIdentifier));

            var signal = new SignalDto
            {
                Id = Guid.NewGuid(),
                SignalIdentifier = signalIdentifier,
                Data = data,
                ClientSentTime = DateTime.UtcNow
            };

            return _dispatcher.DispatchAsync(signal);
        }
    }
}
