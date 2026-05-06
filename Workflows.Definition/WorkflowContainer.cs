using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Workflows.Definition
{
    public abstract partial class WorkflowContainer
    {
        public abstract IAsyncEnumerable<Wait> ExecuteWorkflowAsync();
        protected CompensationWait Compensate(
            string compasenationToken,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            return new CompensationWait(
                compasenationToken,
                WaitType.Compensation,
                inCodeLine,
                callerName,
                callerFilePath)
            {
                WorkflowContainer = this
            };
        }

        internal Dictionary<string, object> Variables { get; set; } = new Dictionary<string, object>();
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