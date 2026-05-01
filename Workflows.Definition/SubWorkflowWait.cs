using System;
using System.Collections.Generic;
using System.Reflection;
using Workflows.Abstraction.DTOs;

namespace Workflows.Handler.BaseUse
{
    /// <summary>
    /// Represents a passive wait for a sub-workflow to complete.
    /// Sub-workflows are containers for other waits and do not initiate
    /// side effects themselves, so they can be safely combined with other passive waits.
    /// </summary>
    public class SubWorkflowWait : Wait, IPassiveWait
    {
        internal MethodInfo SubWorkflowMethodInfo { get; set; }
        internal WaitInfrastructureDto FirstWait { get; set; }
        internal IAsyncEnumerator<Wait> Runner { get; set; }
        public HashSet<string> CancelTokens { get; set; }

        public SubWorkflowWait WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens ??= new HashSet<string>();
            CancelTokens.Add(token);
            return this;
        }

        IPassiveWait IPassiveWait.WithCancelToken(string token) => WithCancelToken(token);

        internal SubWorkflowWait(SubWorkflowWaitDto wait) : base(wait)
        {
        }
    }
}