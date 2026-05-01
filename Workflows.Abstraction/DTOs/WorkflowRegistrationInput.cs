using System;
using System.Collections.Generic;
using System.Reflection;

namespace Workflows.Abstraction.DTOs
{
    public class WorkflowRegistrationInput
    {
        public WorkflowRegistrationInput(string workflowIdentifier, MethodInfo workflowMethod)
        {
            WorkflowIdentifier = workflowIdentifier;
            WorkflowMethodInfo = GetWorkflowMethodInfo(workflowMethod);
        }

        public string WorkflowIdentifier { get; }
        public WorkflowMethodInfo WorkflowMethodInfo { get; }
        private WorkflowMethodInfo GetWorkflowMethodInfo(MethodInfo workflowMethod)
        {
            throw new NotImplementedException();
        }
    }
}