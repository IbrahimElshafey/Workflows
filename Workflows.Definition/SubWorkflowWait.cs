using System.Collections.Generic;
using System.Reflection;

namespace Workflows.Definition
{
    public class SubWorkflowWait : Definition.Wait, Definition.IPassiveWait
    {
        internal MethodInfo SubWorkflowMethodInfo { get; set; }
        internal Wait FirstWait { get; set; }
        internal IAsyncEnumerator<Definition.Wait> Runner { get; set; }

        internal SubWorkflowWait(string waitName, int inCodeLine, string callerName)
            : base(WaitType.SubWorkflowWait, waitName, inCodeLine, callerName)
        {
        }

        public HashSet<string> CancelTokens { get; set; }

        public Definition.SubWorkflowWait WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens ??= new HashSet<string>();
            CancelTokens.Add(token);
            return this;
        }

        Definition.IPassiveWait Definition.IPassiveWait.WithCancelToken(string token) => WithCancelToken(token);
    }
}

