using System;
using System.Threading.Tasks;

namespace Workflows.Abstraction.Orchestrator
{
    public interface IExternalScheduler
    {
        /// <summary>
        /// Schedules a Signal to be sent back to the Orchestrator 
        /// after a specific delay.
        /// </summary>
        Task ScheduleSignalAsync(Guid timerId, string signalIdentifier, object payload, DateTime executeAt);

        /// <summary>
        /// Cancels a pending scheduled signal trigger.
        /// </summary>
        Task CancelScheduledSignalAsync(Guid timerId);

    }
}
