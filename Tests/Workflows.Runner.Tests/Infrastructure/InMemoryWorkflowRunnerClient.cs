using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;

namespace Workflows.Runner.Tests.Infrastructure
{
    /// <summary>
    /// In-memory workflow runner client for testing (no-op)
    /// </summary>
    internal class InMemoryWorkflowRunnerClient : IWorkflowRunnerClient
    {
        public List<(AsyncResult RunId, WorkflowExecutionResponse Result)> SentResults { get; } = new();

        public Task<AsyncResult> SendWorkflowRunResultAsync(AsyncResult runId, WorkflowExecutionResponse result, CancellationToken cancellationToken = default)
        {
            SentResults.Add((runId, result));
            return Task.FromResult(new AsyncResult(
                Guid.NewGuid(),
                result,
                "Accepted",
                "Result sent",
                DateTime.UtcNow));
        }
    }
}
