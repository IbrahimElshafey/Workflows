using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;

namespace Workflows.Hosting.InProcess
{
    public class StateDelta
    {
        public AsyncResult RunResult { get; }
        public WorkflowExecutionResponse Response { get; }
        public TaskCompletionSource<AsyncResult>? CompletionSource { get; }

        public StateDelta(
            AsyncResult runResult,
            WorkflowExecutionResponse response,
            TaskCompletionSource<AsyncResult>? completionSource)
        {
            RunResult = runResult;
            Response = response;
            CompletionSource = completionSource;
        }
    }
}
