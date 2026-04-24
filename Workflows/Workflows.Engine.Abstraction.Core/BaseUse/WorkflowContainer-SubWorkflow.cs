using Workflows.Handler.BaseUse;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using System;
using System.Collections.Generic;
namespace Workflows.Handler
{
    public abstract partial class WorkflowContainer
    {
        protected Wait WaitSubWorkflow(
            IAsyncEnumerable<Wait> workflow,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            var runner = workflow.GetAsyncEnumerator();
            var runnerName = workflow.GetAsyncEnumerator().GetType().Name;
            var workflowName = Regex.Match(runnerName, "<(.+)>").Groups[1].Value;
            var workflowInfo = GetType().GetMethod(workflowName, CoreExtensions.DeclaredWithinTypeFlags());
            return new WorkflowWaitEntity
            {
                Name = name ?? $"#Wait Workflow `{workflowName}`",
                WaitType = WaitType.WorkflowWait,
                WorkflowInfo = workflowInfo,
                CurrentWorkflow = this,
                CallerName = callerName,
                InCodeLine = inCodeLine,
                Runner = runner,
                Created = DateTime.UtcNow,
            }.ToWait();
        }
    }
}