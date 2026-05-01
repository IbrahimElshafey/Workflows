using System;
using System.Threading.Tasks;

namespace Workflows.Abstraction.Orchestrator
{
    public interface IOrchestrator
    {
        /// <summary>
        /// Entry point for external signals (Webhooks, API, Service Bus).
        /// Finds matching instances and triggers the Runner.
        /// </summary>
        Task ProcessSignalAsync(string signalPath, object payload);

        /// <summary>
        /// Entry point for Command results returning from external systems.
        /// </summary>
        Task ProcessCommandResultAsync(Guid commandWaitId, object result);

        /// <summary>
        /// Starts a brand new instance of a workflow.
        /// </summary>
        Task<Guid> StartWorkflowAsync(string workflowName, string version, object input);
    }
}
