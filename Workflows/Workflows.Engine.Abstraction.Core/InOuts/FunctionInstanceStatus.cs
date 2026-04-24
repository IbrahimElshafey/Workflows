namespace Workflows.Handler.InOuts
{
    public enum WorkflowInstanceStatus
    {
        New = 0,
        InProgress = 1,
        Completed = 2,
        InError = 3,
        Canceled = 4
    }
}