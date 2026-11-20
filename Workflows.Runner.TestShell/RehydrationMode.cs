namespace Workflows.TestShell
{
    /// <summary>
    /// Controls how <see cref="WorkflowTestShell"/> treats the runner between two yields.
    /// </summary>
    public enum RehydrationMode
    {
        /// <summary>
        /// Keep one service provider - and therefore one runner, workflow registry and command
        /// handler factory - alive for the whole workflow.
        /// </summary>
        Direct = 0,

        /// <summary>
        /// Throw the service provider away after every yield and build a fresh one before the next
        /// resume. Only the serialized state survives, which is what a worker restart looks like.
        /// </summary>
        Rehydrating = 1
    }
}