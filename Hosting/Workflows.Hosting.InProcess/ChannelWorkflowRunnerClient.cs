using System;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;

namespace Workflows.Hosting.InProcess
{
    public class ChannelWorkflowRunnerClient : IWorkflowRunnerClient
    {
        private readonly WorkflowExecutionChannel _channel;
        private readonly WorkflowExecutionSession _session;

        public ChannelWorkflowRunnerClient(
            WorkflowExecutionChannel channel,
            WorkflowExecutionSession session)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public async Task<AsyncResult> SendWorkflowRunResultAsync(
            AsyncResult runResult,
            WorkflowExecutionResponse result,
            CancellationToken cancellationToken = default)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            // Create a per-call TCS so this method blocks until CoordinatorCommitWorker
            // finishes the DB commit. This ensures state is visible to callers immediately
            // after StartWorkflow or RunWorkflow returns.
            var tcs = new TaskCompletionSource<AsyncResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            // If the session already has a completion source (set by RunnerWorker for the ingress path),
            // chain it: when the coordinator commits, signal both.
            var outerTcs = _session.CompletionSource;
            TaskCompletionSource<AsyncResult> coordinatorTcs = outerTcs != null ? outerTcs : tcs;

            // When outer == null, we only have our local tcs; re-point coordinatorTcs to it.
            var delta = new StateDelta(runResult, result, outerTcs ?? tcs);
            await _channel.EgressWriter.WriteAsync(delta, cancellationToken);

            // If this call supplied its own TCS (no outer), wait for coordinator acknowledgment.
            if (outerTcs == null)
            {
                return await tcs.Task;
            }

            return runResult;
        }
    }
}
