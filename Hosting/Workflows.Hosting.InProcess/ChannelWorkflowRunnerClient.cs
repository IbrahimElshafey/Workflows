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

            var delta = new StateDelta(runResult, result, _session.CompletionSource);
            await _channel.EgressWriter.WriteAsync(delta, cancellationToken);

            return runResult;
        }
    }
}
