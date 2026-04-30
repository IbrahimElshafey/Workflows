using System;
using System.Collections.Generic;
using Workflows.Handler.BaseUse;

namespace Workflows.Runner.Registration
{
    public class WorkflowRegistrationInput
    {
        public WorkflowRegistrationInput(string workflowIdentifier, Func<IAsyncEnumerable<Wait>> workflowMethod)
        {
            WorkflowIdentifier = workflowIdentifier;
            WorkflowMethodInfo = GetWorkflowMethodInfo(workflowMethod);
        }

        public string WorkflowIdentifier { get; }
        public WorkflowMethodInfo WorkflowMethodInfo { get; }
        private WorkflowMethodInfo GetWorkflowMethodInfo(Func<IAsyncEnumerable<Wait>> workflowMethod)
        {
            throw new NotImplementedException();
        }
    }
}