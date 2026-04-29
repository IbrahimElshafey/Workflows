using Workflows.Handler.BaseUse;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using System;
using System.Collections.Generic;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.DTOs;
namespace Workflows.Handler
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
            var workflowInfo = GetType().GetMethod(workflowName, Abstraction.Helpers.CoreExtensions.DeclaredWithinTypeFlags());
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