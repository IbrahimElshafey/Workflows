namespace Workflows.Abstraction.Runner
{
    /// <summary>
    /// Execution limits for the workflow runner.
    /// Register an instance before calling AddWorkflowsRunner to override the defaults.
    /// </summary>
    public sealed class WorkflowRunnerOptions
    {
        /// <summary>
        /// Maximum nesting depth of sub-workflows. Execution fails loudly when the limit is exceeded.
        /// Set to zero or a negative value to disable the guard.
        /// </summary>
        public int MaxSubWorkflowDepth { get; set; } = 100;
    }
}