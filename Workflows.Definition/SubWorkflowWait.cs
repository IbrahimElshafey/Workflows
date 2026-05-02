using System.Collections.Generic;
using System.Reflection;
using Workflows.Definition.Data.DTOs;

namespace Workflows.Definition
{
    /// <summary>
    /// Represents a passive wait for a sub-workflow to complete.
    /// Sub-workflows are containers for other waits and do not initiate
    /// side effects themselves, so they can be safely combined with other passive waits.
    /// </summary>
    public class SubWorkflowWait : Definition.Wait, Definition.IPassiveWait
    {
        internal MethodInfo SubWorkflowMethodInfo { get; set; }
        internal WaitInfrastructureDto FirstWait { get; set; }
        internal IAsyncEnumerator<Definition.Wait> Runner { get; set; }
        public HashSet<string> CancelTokens { get; set; }

        public Definition.SubWorkflowWait WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens ??= new HashSet<string>();
            CancelTokens.Add(token);
            return this;
        }

        Definition.IPassiveWait Definition.IPassiveWait.WithCancelToken(string token) => WithCancelToken(token);

        internal SubWorkflowWait(SubWorkflowWaitDto wait) : base(wait)
        {
        }
    }
}