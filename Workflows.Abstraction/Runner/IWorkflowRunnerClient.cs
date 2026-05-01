using System.Threading;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;

namespace Workflows.Abstraction.Runner
{
    /// <summary>
    /// Acts as the communication bridge to send execution results from the Runner 
    /// back to the Orchestrator for persistence and signal matching.
    /// </summary>
    public interface IWorkflowRunnerClient
    {
        /// <summary>
        /// Asynchronously pushes the result of a workflow execution step to the Orchestrator.
        /// </summary>
        /// <param name="runId">The unique identifier for the workflow run.</param>
        /// <param name="result">The outcome of the execution, including new waits and updated state.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the asynchronous operation.</param>
        /// <returns>A task representing the asynchronous send operation.</returns>
        Task<SendId> SendWorkflowRunResultAsync(WorkflowRunId runId, WorkflowRunResult result, CancellationToken cancellationToken = default);
    }
}