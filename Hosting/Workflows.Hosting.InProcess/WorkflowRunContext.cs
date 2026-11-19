using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;

namespace Workflows.Hosting.InProcess
{
    public class WorkflowRunContext
    {
        public object Message { get; }
        public TaskCompletionSource<AsyncResult>? CompletionSource { get; }

        public WorkflowRunContext(object message, TaskCompletionSource<AsyncResult>? completionSource)
        {
            Message = message;
            CompletionSource = completionSource;
        }
    }
}
