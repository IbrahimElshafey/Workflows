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
        PlaceholderSubWorkflow,
        /// <summary>
        /// Massive fan-out: all external child waits must complete.
        /// </summary>
        WaitMany,
        /// <summary>
        /// Massive fan-out: any single external child wait completes.
        /// </summary>
        WaitAny
    }
}
