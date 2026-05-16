namespace Workflows.Primitives
{
    /// <summary>
    /// Defines who receives the command result and how execution resumes.
    /// </summary>
    public enum CommandExecutionMode
    {
        /// <summary>
        /// The runner receives the result directly and immediately continues to the next step
        /// without persisting state or involving the orchestrator.
        /// </summary>
        ImmediateCommand,

        /// <summary>
        /// The orchestrator receives the result as an incoming signal and reactivates
        /// the runner later, allowing the workflow state to be persisted between invocations.
        /// </summary>
        DeferredCommand
    }
}
