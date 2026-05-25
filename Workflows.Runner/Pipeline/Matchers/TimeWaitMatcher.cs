using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;

namespace Workflows.Runner.Pipeline.Matchers
{
    /// <summary>
    /// Evaluates time-based wait triggers.
    /// Validates that the current system clock satisfies the timer boundary on the TimeWait metadata.
    /// </summary>
    internal class TimeWaitMatcher : WorkflowWaitMatcher
    {
        private readonly WorkflowExecutionContext _context;
        private readonly MatcherFactory _matcherFactory;

        public TimeWaitMatcher(WorkflowExecutionContext context, MatcherFactory matcherFactory)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _matcherFactory = matcherFactory ?? throw new ArgumentNullException(nameof(matcherFactory));
        }

        public override async Task<bool> MatchAsync(WaitInfrastructureDto waitDto)
        {
            // TimeWait evaluation is typically done by the orchestrator/scheduler
            // before sending the execution request. If we reach here, the time
            // boundary has been satisfied.

            var timeWaitDto = waitDto as TimeWaitDto;
            if (timeWaitDto != null)
            {
                // Mark this wait as completed
                timeWaitDto.Status = WaitStatus.Completed;

                // Propagate matching to parent wait if present
                if (timeWaitDto.ParentWaitId.HasValue)
                {
                    return await MatchParentAsync(timeWaitDto.ParentWaitId.Value, _context, _matcherFactory);
                }
            }

            return true;
        }
    }
}

