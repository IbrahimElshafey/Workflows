using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Workflows.Definition
{
    public abstract partial class WorkflowContainer
    {
        protected SubWorkflowWait WaitSubWorkflow(
            IAsyncEnumerable<Wait> workflow,
            string name = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            var result = new SubWorkflowWait(name, inCodeLine, callerName, callerFilePath)
            {
                WorkflowContainer = this,
                Runner = workflow,
                WaitType = WaitType.SubWorkflowWait
            };
            return result;
        }
    }
}
