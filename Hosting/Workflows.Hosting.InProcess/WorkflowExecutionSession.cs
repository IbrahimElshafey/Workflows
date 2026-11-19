using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;

namespace Workflows.Hosting.InProcess
{
    public class WorkflowExecutionSession
    {
        public TaskCompletionSource<AsyncResult>? CompletionSource { get; set; }
    }
}
