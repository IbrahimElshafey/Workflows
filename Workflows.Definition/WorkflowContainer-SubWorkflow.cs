
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Workflows.Definition.Helpers;

namespace Workflows.Definition
{
    public abstract partial class WorkflowContainer
    {
        protected Definition.SubWorkflowWait WaitSubWorkflow(
            IAsyncEnumerable<Definition.Wait> workflow,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            var runner = workflow.GetAsyncEnumerator();
            var runnerName = runner.GetType().Name;
            var workflowName = Regex.Match(runnerName, "<(.+)>").Groups[1].Value;
            var workflowInfo = GetType().GetMethod(workflowName, CoreExtensions.DeclaredWithinTypeFlags());
            var result = new Definition.SubWorkflowWait(new DTOs.SubWorkflowWaitDto
            {
                WaitName = name ?? $"#Wait Workflow `{workflowName}`",
                WaitType = Enums.WaitType.SubWorkflowWait,
                CallerName = callerName,
                InCodeLine = inCodeLine,
                Created = DateTime.UtcNow,
            })
            {
                CurrentWorkflow = this,
                SubWorkflowMethodInfo = workflowInfo,
                Runner = runner
            };
            return result;
        }
    }
}