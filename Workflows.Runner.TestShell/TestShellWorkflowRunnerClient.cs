using System;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;

namespace Workflows.TestShell
{
    public class TestShellWorkflowRunnerClient : IWorkflowRunnerClient
    {
        private readonly Action<AsyncResult, WorkflowExecutionResponse> _onResultSent;

        public TestShellWorkflowRunnerClient(Action<AsyncResult, WorkflowExecutionResponse> onResultSent)
        {
            _onResultSent = onResultSent ?? throw new ArgumentNullException(nameof(onResultSent));
        }

        public Task<AsyncResult> SendWorkflowRunResultAsync(
            AsyncResult runId, 
            WorkflowExecutionResponse result, 
            CancellationToken cancellationToken = default)
        {
            _onResultSent(runId, result);
            return Task.FromResult(new AsyncResult(
                runId.Id,
                result,
                "Accepted",
                "Successfully simulated runner result saving.",
                DateTime.UtcNow));
        }
    }
}
