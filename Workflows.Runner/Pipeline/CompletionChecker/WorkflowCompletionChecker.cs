using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Runner.Pipeline.CompletionChecker
{
    /// <summary>
    /// Completion checker for SubWorkflowWait - simply checks if the sub-workflow status is Completed.
    /// The sub-workflow execution/resumption is driven by the runner.
    /// </summary>
    internal class WorkflowCompletionChecker : WaitCompletionChecker
    {
        public override Task<bool> IsCompleted(WaitInfrastructureDto waitDto)
        {
            if (waitDto == null) throw new ArgumentNullException(nameof(waitDto));
            return Task.FromResult(waitDto.Status == Abstraction.Enums.WaitStatus.Completed);
        }
    }
}
