using Workflows.Handler.InOuts.Entities;
namespace Workflows.Handler.UiService.InOuts
{
    public class WorkflowInfo
    {
        public WorkflowInfo(WorkflowIdentifier workflowIdentifier, string firstWait, int inProgress, int completed, int failed)
        {
            WorkflowIdentifier = workflowIdentifier;
            FirstWait = firstWait;
            InProgress = inProgress;
            Completed = completed;
            Failed = failed;
        }

        public WorkflowIdentifier WorkflowIdentifier { get; }
        public string FirstWait { get; }
        public int InProgress { get; }
        public int Completed { get; }
        public int Failed { get; }
    }
}
