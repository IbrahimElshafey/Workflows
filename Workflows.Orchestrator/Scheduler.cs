using Workflows.Abstraction.Orchestrator;

namespace Workflows.Orchestrator
{
    public class Scheduler : IExternalScheduler
    {
        public Task ScheduleSignalAsync(string signalIdentifier, object payload, DateTime executeAt)
        {
            throw new NotImplementedException();
        }
    }
}
