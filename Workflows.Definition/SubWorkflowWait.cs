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
        internal SubWorkflowWait(SubWorkflowWaitDto wait) : base(wait)
        {
        }
    }
}