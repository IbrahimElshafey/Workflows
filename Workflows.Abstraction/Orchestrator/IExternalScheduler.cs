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
        Task ScheduleSignalAsync(string signalIdentifier, object payload, DateTime executeAt);
    }
}
