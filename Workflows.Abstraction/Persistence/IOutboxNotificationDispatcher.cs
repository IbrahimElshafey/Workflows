using System;
using Workflows.Abstraction.DTOs;

namespace Workflows.Abstraction.Persistence
{
    public interface IOutboxNotificationDispatcher
    {
        void NotifyCommandDispatched(CommandDispatchNotification notification);
        void NotifyCompensationRequired(Guid instanceId);
        void NotifyCancellationRequested(Guid instanceId, string token, string reason);
    }
}
