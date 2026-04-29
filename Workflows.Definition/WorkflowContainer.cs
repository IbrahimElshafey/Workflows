using System;
using System.Threading.Tasks;
namespace Workflows.Handler
{
    public abstract partial class WorkflowContainer
    {
        public virtual Task OnError(string message, Exception ex = null)
        {
            return Task.CompletedTask;
        }
        public virtual Task OnCompleted()
        {
            return Task.CompletedTask;
        }
    }
}