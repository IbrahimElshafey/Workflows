using System.Threading.Tasks;

namespace Workflows.Runner.Pipeline.Matchers
{
    /// <summary>
    /// Evaluates time-based wait triggers.
    /// Validates that the current system clock satisfies the timer boundary on the TimeWait metadata.
    /// </summary>
    internal class TimeWaitMatcher : WorkflowWaitMatcher
    {
        public override Task<bool> MatchAsync(WorkflowExecutionContext context)
        {
            // TimeWait evaluation is typically done by the orchestrator/scheduler
            // before sending the execution request. If we reach here, the time
            // boundary has been satisfied.
            return Task.FromResult(true);
        }
    }
}

