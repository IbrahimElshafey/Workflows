namespace Workflows.Definition.Data.Enums
{
    public enum WaitType
    {
        SignalWait,
        GroupWaitAll,
        GroupWaitFirst,
        GroupWaitWithExpression,
        SubWorkflowWait,
        Command,
        CommandsGroup
    }
}