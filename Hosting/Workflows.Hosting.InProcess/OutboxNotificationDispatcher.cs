using System;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Persistence;

namespace Workflows.Hosting.InProcess
{
    public class OutboxNotificationDispatcher : IOutboxNotificationDispatcher
    {
        private readonly BackgroundWorkerChannel _channel;

        public OutboxNotificationDispatcher(BackgroundWorkerChannel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        }

        public void NotifyCommandDispatched(CommandDispatchNotification notification)
        {
            _channel.CommandWriter.TryWrite(notification);
        }

        public void NotifyCompensationRequired(Guid instanceId)
        {
            _channel.CompensationWriter.TryWrite(new CompensationRequest { WorkflowInstanceId = instanceId });
        }

        public void NotifyCancellationRequested(Guid instanceId, string token, string reason)
        {
            _channel.CancellationWriter.TryWrite(new CancellationRequest 
            { 
                WorkflowInstanceId = instanceId, 
                Token = token, 
                Reason = reason 
            });
        }
    }
}
