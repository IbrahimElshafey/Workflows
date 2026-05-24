using System;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Client.Internal;

namespace Workflows.Client
{
    /// <summary>
    /// Extension methods for registering the Workflows client in an
    /// <see cref="IServiceCollection"/>.
    /// </summary>
    public static class WorkflowsClientServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the core Workflows client services:
        /// <list type="bullet">
        ///   <item><see cref="IWorkflowSignalClient"/> → <see cref="WorkflowSignalClient"/></item>
        ///   <item><see cref="Internal.CommandExecutorRegistry"/> (singleton)</item>
        ///   <item><see cref="Internal.CommandExecutorWorker"/> (hosted background service)</item>
        /// </list>
        /// Returns a <see cref="WorkflowsClientBuilder"/> for chaining
        /// <c>AddCommandExecutor&lt;,,&gt;</c> registrations.
        /// </summary>
        /// <remarks>
        /// <b>Prerequisites</b>: The calling project must independently register an
        /// <c>IMessageDispatcher</c>, <c>IMessageSubscriber</c>, and
        /// <c>IObjectSerializer</c> — typically via a transport-specific package
        /// (e.g., <c>AddWorkflowsInProcessHost</c>, or a future Service Bus / HTTP package).
        /// </remarks>
        /// <example>
        /// <code>
        /// services.AddWorkflowsClient()
        ///         .AddCommandExecutor&lt;SendEmailCommand, SendEmailResult, SendEmailExecutor&gt;("EmailService.Send")
        ///         .AddCommandExecutor&lt;ChargeCardCommand, ChargeResult, ChargeExecutor&gt;("Payments.Charge");
        /// </code>
        /// </example>
        public static WorkflowsClientBuilder AddWorkflowsClient(this IServiceCollection services)
        {
            if (services is null) throw new ArgumentNullException(nameof(services));

            // Signal client — transient is fine; it holds no state.
            services.AddTransient<IWorkflowSignalClient, WorkflowSignalClient>();

            // Singleton registry populated during builder configuration.
            var registry = new CommandExecutorRegistry();
            services.AddSingleton(registry);

            // Background service that drives the command execution loop.
            services.AddHostedService<CommandExecutorWorker>();

            return new WorkflowsClientBuilder(services, registry);
        }
    }
}
