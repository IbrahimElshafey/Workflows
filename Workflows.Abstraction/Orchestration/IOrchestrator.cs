using System;
using System.Threading.Tasks;

namespace Workflows.Abstraction.Orchestration
{
    public interface IOrchestrator
    {
        Task ProcessSignalAsync<TPayload>(string signalIdentifier, TPayload payload);
    }
}
