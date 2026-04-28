namespace Workflows.Abstraction.Enums
{
    public enum WorkflowInstanceStatus
    {
        New = 0,
        InProgress = 100,
        Completed = 200,
        InError = 300,
        Canceled = 400,
        Running = 500,
    }
}