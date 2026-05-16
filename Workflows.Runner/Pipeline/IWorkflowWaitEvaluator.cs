using System.Threading.Tasks;

namespace Workflows.Runner.Pipeline
{
    /// <summary>
    /// Base class for evaluators that validate incoming events (signal, timer, command result) against 
    /// wait constraints before advancing the state machine. Returns false if conditions fail or match 
    /// partially, halting execution immediately.
    /// </summary>
    internal abstract class WorkflowWaitEvaluator
    {
        /// <summary>
        /// Evaluates the incoming event against the wait. Returns true if evaluation succeeds
        /// and the workflow should proceed; false if evaluation fails or is incomplete.
        /// </summary>
        public abstract Task<bool> EvaluateAsync(WorkflowExecutionContext context);
    }
}
