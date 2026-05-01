using System;
using System.Reflection;

namespace Workflows.Abstraction.DTOs
{
    public class WorkflowRegistrationInput
    {
        public WorkflowRegistrationInput(string workflowIdentifier, MethodInfo workflowMethod, string version)
        {
            WorkflowIdentifier = workflowIdentifier;
            WorkflowMethodInfo = GetWorkflowMethodInfo(workflowMethod);
            Version = version;
        }

        public string Version { get; }
        public string WorkflowIdentifier { get; }
        public WorkflowMethodInfo WorkflowMethodInfo { get; }
        private WorkflowMethodInfo GetWorkflowMethodInfo(MethodInfo workflowMethod)
        {
            throw new NotImplementedException();
        }
    }
}