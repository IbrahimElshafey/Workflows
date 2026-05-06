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
        CommandsGroup,
        Compensation
    }
}
