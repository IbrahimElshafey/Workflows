namespace Workflows.Definition
{
    /// <summary>
    /// Resolves an ICommandHandler based on a handler key.
    /// </summary>
    public interface ICommandHandlerFactory
    {
        /// <summary>
        /// Returns the command handler registered for the given key.
        /// </summary>
        Definition.ICommandHandler GetHandler(string handlerKey);
    }
}
