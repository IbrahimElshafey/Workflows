using System.Collections.Generic;
using Workflows.Primitives;

namespace Workflows.Definition
{
    public class SubWorkflowWait : Wait
    {
        internal Wait FirstWait { get; set; }
        internal IAsyncEnumerable<Wait> Runner { get; set; }

        internal SubWorkflowWait(string waitName, int inCodeLine, string callerName, string callerFilePath)
            : base(WaitType.SubWorkflowWait, waitName, inCodeLine, callerName, callerFilePath)
        {
        }

        internal SubWorkflowWait()
        {
        }

        public SubWorkflowWait WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens.Add(token);
            return this;
        }
    }
}

