namespace Workflows.Abstraction.Runner
{
    /// <summary>
    /// Resolves command handlers based on a handler key.
    /// </summary>
    public interface ICommandHandlerFactory
    {
        /// <summary>
        /// Returns the immediate command handler registered for the given key.
        /// </summary>
        object GetHandler(string handlerKey);
    }
}
