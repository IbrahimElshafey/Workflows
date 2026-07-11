namespace Workflows.Abstraction.Runner
{
    /// <summary>
    /// Marker interface for a command whose handler runs inside the Runner process
    /// and returns a result synchronously. The runner awaits and continues without
    /// suspending the workflow.
    /// </summary>
    public interface IImmediateCommand<TInput, TResult>
    {
    }
}
