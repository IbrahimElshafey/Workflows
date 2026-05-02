using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Workflows.Definition.Helpers;

namespace Workflows.Definition
{
    public abstract partial class WorkflowContainer
    {
        protected SubWorkflowWait WaitSubWorkflow(
            IAsyncEnumerable<Wait> workflow,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            var runner = workflow.GetAsyncEnumerator();
            var runnerName = runner.GetType().Name;
            var workflowName = Regex.Match(runnerName, "<(.+)>").Groups[1].Value;
            var workflowInfo = GetType().GetMethod(workflowName, CoreExtensions.DeclaredWithinTypeFlags());
            var result = new SubWorkflowWait(
                name ?? $"#Wait Workflow `{workflowName}`",
                inCodeLine,
                callerName)
            {
                CurrentWorkflow = this,
                SubWorkflowMethodInfo = workflowInfo,
                Runner = runner,
                WaitType = WaitType.SubWorkflowWait
            };
            return result;
        }
    }
}
