using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Workflows.Primitives;

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
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Wait name is mandatory.");
            }
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
