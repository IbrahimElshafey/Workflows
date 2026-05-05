using System;
using System.Reflection;

namespace Workflows.Abstraction.DTOs.Registration
{
    public class WorkflowRegistrationInput
    {
        public WorkflowRegistrationInput(string workflowName, MethodInfo workflowMethod, string version)
        {
            WorkflowName = workflowName;
            WorkflowMethodInfo = GetWorkflowMethodInfo(workflowMethod);
            Version = version;
        }

        public string Version { get; }
        public string WorkflowName { get; }
        public WorkflowMethodInfo WorkflowMethodInfo { get; }
        private WorkflowMethodInfo GetWorkflowMethodInfo(MethodInfo workflowMethod)
        {
            throw new NotImplementedException();
        }
    }
}