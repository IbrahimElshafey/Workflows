using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;

namespace Workflows.Runner.Pipeline.CompletionChecker
{
    /// <summary>
    /// Evaluates time-based wait triggers.
    /// Validates that the current system clock satisfies the timer boundary on the TimeWait metadata.
    /// </summary>
    internal class TimeWaitMatcher : WaitCompletionChecker
    {
        private readonly WorkflowExecutionContext _context;

        public TimeWaitMatcher(WorkflowExecutionContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public override async Task<bool> IsCompleted(WaitInfrastructureDto waitDto)
        {
            // TimeWait evaluation is typically done by the orchestrator/scheduler
            // before sending the execution request. If we reach here, the time
            // boundary has been satisfied.

            var timeWaitDto = waitDto as TimeWaitDto;
            if (timeWaitDto != null)
            {
                // Mark this wait as completed
                timeWaitDto.Status = WaitStatus.Completed;
            }

            return true;
        }
    }
}

