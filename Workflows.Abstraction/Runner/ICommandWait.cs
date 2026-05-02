using Workflows.Abstraction.Enums;

namespace Workflows.Abstraction.Runner
{
    /// <summary>
    /// Marker interface for active waits that initiate side effects (e.g., commands, API calls).
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
        CommandExecutionMode ExecutionMode { get; }
    }
}
