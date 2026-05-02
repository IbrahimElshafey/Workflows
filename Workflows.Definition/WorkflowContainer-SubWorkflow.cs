
using System;
using System.Text.RegularExpressions;
using Workflows.Definition.Data.Enums;
using Workflows.Definition.Helpers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Workflows.Definition.Data.DTOs;

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
            var result = new SubWorkflowWait(new SubWorkflowWaitDto
            {
                WaitName = name ?? $"#Wait Workflow `{workflowName}`",
                WaitType = WaitType.SubWorkflowWait,
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