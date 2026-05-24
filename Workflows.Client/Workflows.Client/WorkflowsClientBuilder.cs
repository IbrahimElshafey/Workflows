using Microsoft.Extensions.DependencyInjection;
using Workflows.Client.Internal;

namespace Workflows.Client
{
    /// <summary>
    /// Fluent builder returned by <see cref="WorkflowsClientServiceCollectionExtensions.AddWorkflowsClient"/>.
    /// Use <see cref="AddCommandExecutor{TCommand,TResult,TExecutor}"/> to register
    /// each deferred command handler.
    /// </summary>
    public sealed class WorkflowsClientBuilder
    {
        internal IServiceCollection Services { get; }
        internal CommandExecutorRegistry Registry { get; }

        internal WorkflowsClientBuilder(IServiceCollection services, CommandExecutorRegistry registry)
        {
            Services = services;
            Registry = registry;
        }

        /// <summary>
        /// Registers a strongly-typed command executor so that
        /// <see cref="Internal.CommandExecutorWorker"/> can route incoming
        /// <c>CommandDispatchNotification</c> messages to it.
        /// </summary>
        /// <typeparam name="TCommand">Command payload type.</typeparam>
        /// <typeparam name="TResult">Result payload type.</typeparam>
        /// <typeparam name="TExecutor">
        /// Concrete <see cref="ICommandExecutor{TCommand,TResult}"/> implementation.
        /// </typeparam>
        /// <param name="handlerKey">
        /// Must match the handler key supplied during workflow command registration
        /// (e.g., <c>"EmailService.Send"</c>).
        /// </param>
        public WorkflowsClientBuilder AddCommandExecutor<TCommand, TResult, TExecutor>(string handlerKey)
            where TExecutor : class, ICommandExecutor<TCommand, TResult>
        {
            // Register the concrete executor so DI can resolve it inside scopes.
            Services.AddScoped<TExecutor>();

            // Build the adapter once — no runtime reflection on the hot path.
            Registry.Register(handlerKey, new CommandExecutorDescriptor
            {
                CommandType = typeof(TCommand),
                ResultType  = typeof(TResult),
                Execute     = async (sp, commandData, ct) =>
                {
                    var executor = sp.GetRequiredService<TExecutor>();
                    var result   = await executor.ExecuteAsync((TCommand)commandData, ct)
                                                 .ConfigureAwait(false);
                    return (object)result!;
                }
            });

            return this;
        }
    }
}
