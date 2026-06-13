namespace Workflows.Primitives
{
    public enum WaitType
    {
        SignalWait,
        GroupWaitAll,
        GroupWaitFirst,
        GroupWaitWithExpression,
        SubWorkflowWait,
        Command,
        Compensation,
        Placeholder,
        PlaceholderSubWorkflow
    }
}
