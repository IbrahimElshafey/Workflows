using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;

namespace Workflows.Abstraction.Runner
{
    /// <summary>
    /// Represents the stateless compute engine responsible for resuming and 
    /// advancing a workflow's state machine.
    /// </summary>
    public interface IWorkflowRunner
    {
        Task<AsyncResult> StartWorkflow(string workflowName, object intialState = null);
        /// <summary>
        /// Starts a specific version of a workflow. If the version is not available, falls back to the latest version.
        /// </summary>
        Task<AsyncResult> StartWorkflow(string workflowName, int version, object intialState = null);
        /// <summary>
        /// Executes the next step of a workflow based on the provided context, 
        /// which includes the current state and the incoming signal.
        /// </summary>
        /// <param name="runContext">The state and signal data required to resume execution.</param>
        /// <returns>A <see cref="AsyncResult"/> identifying this specific execution attempt.</returns>
        Task<AsyncResult> RunWorkflowAsync(WorkflowExecutionRequest runContext);
    }
}