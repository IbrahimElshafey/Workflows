using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Workflows.Definition
{
    public abstract partial class WorkflowContainer
    {
        internal HashSet<string> TokensToCancel { get; set; } = new HashSet<string>();

        protected void CancelToken(string token)
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                TokensToCancel.Add(token);
            }
        }

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