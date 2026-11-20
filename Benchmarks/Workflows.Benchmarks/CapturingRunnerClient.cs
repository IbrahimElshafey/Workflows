using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;

namespace Workflows.Benchmarks
{
    /// <summary>
    /// Captures the latest state the runner produced so the benchmark can hand it straight back to
    /// <see cref="IWorkflowRunner.RunWorkflowAsync"/> without a workflow store in the loop. The consumed
    /// wait ids are captured too: a real store deletes those rows, and the shell's duplicate-wait-name
    /// guard would reject a loop that kept the completed waits in the snapshot forever.
    /// </summary>
    public sealed class CapturingRunnerClient : IWorkflowRunnerClient
    {
        public WorkflowStateDto? CurrentState { get; private set; }

        public IReadOnlyCollection<string> ConsumedWaitIds { get; private set; } = Array.Empty<string>();

        public Task<AsyncResult> SendWorkflowRunResultAsync(
            AsyncResult runId, WorkflowExecutionResponse result, CancellationToken cancellationToken = default)
        {
            if (result.UpdatedState != null)
            {
                CurrentState = result.UpdatedState;
            }

            ConsumedWaitIds = result.ConsumedWaitsIds ?? new List<string>();

            return Task.FromResult(new AsyncResult(runId.Id, null!, nameof(AsyncStatus.Finished), string.Empty, DateTime.UtcNow));
        }
    }
}