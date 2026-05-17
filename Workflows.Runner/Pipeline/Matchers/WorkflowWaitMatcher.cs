using System.Threading.Tasks;

namespace Workflows.Runner.Pipeline.Matchers
{
    /// <summary>
    /// Base class for matchers that validate incoming events (signal, timer, command result) against 
    /// wait constraints before advancing the state machine. Returns false if conditions fail or match 
    /// partially, halting execution immediately.
    /// </summary>
    internal abstract class WorkflowWaitMatcher
    {
        /// <summary>
        /// Matches the incoming event against the wait. Returns true if the match succeeds
        /// and the workflow should proceed; false if matching fails or is incomplete.
        /// </summary>
        public abstract Task<bool> MatchAsync(WorkflowExecutionContext context);
    }
}
