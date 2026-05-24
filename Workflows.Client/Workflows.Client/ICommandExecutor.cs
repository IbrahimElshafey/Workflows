using System.Threading;
using System.Threading.Tasks;

namespace Workflows.Client
{
    /// <summary>
    /// Defines a strongly-typed executor for a deferred workflow command.
    /// Implement this interface for each command type that is dispatched by the
    /// workflow engine with <c>CommandExecutionMode.Deferred</c>.
    /// </summary>
    /// <typeparam name="TCommand">
    /// The command payload type sent by the workflow (e.g., <c>SendEmailCommand</c>).
    /// </typeparam>
    /// <typeparam name="TResult">
    /// The result type expected by the workflow (e.g., <c>SendEmailResult</c>).
    /// </typeparam>
    public interface ICommandExecutor<TCommand, TResult>
    {
        /// <summary>
        /// Executes the deferred command and returns its result.
        /// The <see cref="CommandExecutorWorker"/> automatically posts the result back
        /// to the Orchestrator via <c>IMessageDispatcher</c> after this method returns.
        /// </summary>
        /// <param name="command">The deserialized command payload.</param>
        /// <param name="cancellationToken">Token to cancel the execution.</param>
        Task<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken = default);
    }
}
