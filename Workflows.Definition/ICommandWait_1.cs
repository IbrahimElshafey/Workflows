namespace Workflows.Definition
{
    /// <summary>
    /// Marker interface for active waits that initiate side effects (e.g., commands, API calls).
    /// Active waits must not be combined with MatchAny() to prevent race conditions where
    /// multiple commands could execute. They should only use MatchAll() via ExecuteParallel().
    /// Examples: Command
    /// </summary>
    public interface ICommandWait
    {

        /// <summary>
        /// The key used to resolve the appropriate handler from ICommandHandlerFactory.
        /// </summary>
        string HandlerKey { get; }

        /// <summary>
        /// Determines whether the command runs fast (auto-advance) or slow (suspend and persist).
        /// </summary>
        Enums.CommandExecutionMode ExecutionMode { get; }
    }
}
