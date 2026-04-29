using System.Collections.Generic;
using System.Reflection;
using Workflows.Abstraction.DTOs;

namespace Workflows.Handler.BaseUse
{
    public class SubWorkflowWait : Wait
    {
        internal MethodInfo SubWorkflowMethodInfo { get; set; }
        internal WaitBaseDto FirstWait { get; set; }
        internal IAsyncEnumerator<Wait> Runner { get; set; }
        internal SubWorkflowWait(SubWorkflowWaitDto wait) : base(wait)
        {
        }
    }
}