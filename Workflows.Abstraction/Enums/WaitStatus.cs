namespace Workflows.Abstraction.Enums
{
    public enum WaitStatus
    {
        Waiting = 0,
        Canceled = 1,
        Completed = 2,
        Transient = 4,
        InError = 5
    }
}