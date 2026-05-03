using System.Collections.Generic;

namespace Workflows.Definition
{
    public class SubWorkflowWait : Wait, IPassiveWait
    {
        internal Wait FirstWait { get; set; }
        internal IAsyncEnumerable<Wait> Runner { get; set; }

        internal SubWorkflowWait(string waitName, int inCodeLine, string callerName, string callerFilePath)
            : base(WaitType.SubWorkflowWait, waitName, inCodeLine, callerName, callerFilePath)
        {
        }

        public HashSet<string> CancelTokens { get; set; }

        public SubWorkflowWait WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens ??= new HashSet<string>();
            CancelTokens.Add(token);
            return this;
        }

        IPassiveWait IPassiveWait.WithCancelToken(string token) => WithCancelToken(token);
    }
}

