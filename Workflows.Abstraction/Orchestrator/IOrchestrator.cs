using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;

namespace Workflows.Abstraction.Orchestrator
{
    public interface IOrchestrator
    {
        /// <summary>
        /// Entry point for external signals (Webhooks, API, Service Bus).
        /// Finds matching instances and triggers the Runner.
        /// </summary>
        Task ProcessSignalAsync(SignalDto signalDto);

        /// <summary>
        /// Entry point for Command results returning from external systems.
        /// </summary>
        Task ProcessCommandResultAsync(CommandResultDto commandResultDto);

        /// <summary>
        /// Starts a brand new instance of a workflow.
        /// </summary>
        Task<Guid> StartWorkflowAsync(string workflowName, string version, object input);
    }
}
