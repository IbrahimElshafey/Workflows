using Workflows.Abstraction.DTOs;

namespace Workflows.Abstraction.Runner
{
    /// <summary>
    /// Represents the stateless compute engine responsible for resuming and 
    /// advancing a workflow's state machine.
    /// </summary>
    public interface IWorkflowRunner
    {
        /// <summary>
        /// Executes the next step of a workflow based on the provided context, 
        /// which includes the current state and the incoming signal.
        /// </summary>
        /// <param name="runContext">The state and signal data required to resume execution.</param>
        /// <returns>A <see cref="WorkflowRunId"/> identifying this specific execution attempt.</returns>
        WorkflowRunId RunWorkflow(WorkflowRunContext runContext);
    }
}