using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Helpers;
using Workflows.Communication.Abstraction;

namespace Workflows.Client.Internal
{
    /// <summary>
    /// Hosted background service that drives the full deferred-command execution loop:
    /// <list type="number">
    ///   <item>Subscribes to <see cref="CommandDispatchNotification"/> messages via
    ///         <see cref="IMessageSubscriber"/>.</item>
    ///   <item>Resolves the matching <see cref="ICommandExecutor{TCommand,TResult}"/>
    ///         from the <see cref="CommandExecutorRegistry"/> using
    ///         <c>HandlerKey</c>.</item>
    ///   <item>Deserializes <c>CommandData</c> to the registered <c>TCommand</c> type
    ///         using <see cref="IObjectSerializer"/>.</item>
    ///   <item>Invokes <c>ExecuteAsync</c> on a dedicated DI scope.</item>
    ///   <item>Posts a <see cref="CommandResultDto"/> back to the Orchestrator via
    ///         <see cref="IMessageDispatcher"/>.</item>
    /// </list>
    /// </summary>
    internal sealed class CommandExecutorWorker : BackgroundService
    {
        private readonly IMessageSubscriber _subscriber;
        private readonly IMessageDispatcher _dispatcher;
        private readonly CommandExecutorRegistry _registry;
        private readonly IObjectSerializer _serializer;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CommandExecutorWorker> _logger;

        public CommandExecutorWorker(
            IMessageSubscriber subscriber,
            IMessageDispatcher dispatcher,
            CommandExecutorRegistry registry,
            IObjectSerializer serializer,
            IServiceScopeFactory scopeFactory,
            ILogger<CommandExecutorWorker> logger)
        {
            _subscriber  = subscriber  ?? throw new ArgumentNullException(nameof(subscriber));
            _dispatcher  = dispatcher  ?? throw new ArgumentNullException(nameof(dispatcher));
            _registry    = registry    ?? throw new ArgumentNullException(nameof(registry));
            _serializer  = serializer  ?? throw new ArgumentNullException(nameof(serializer));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger      = logger      ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _subscriber.Subscribe<CommandDispatchNotification>(
                notification => HandleNotificationAsync(notification, stoppingToken));

            // Block until the host signals shutdown.
            return Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { });
        }

        private async Task HandleNotificationAsync(
            CommandDispatchNotification notification,
            CancellationToken stoppingToken)
        {
            var descriptor = _registry.TryGet(notification.HandlerKey);
            if (descriptor is null)
            {
                _logger.LogWarning(
                    "No executor registered for handler key '{HandlerKey}'. " +
                    "CommandWaitId={CommandWaitId}",
                    notification.HandlerKey, notification.CommandWaitId);
                return;
            }

            object result;
            try
            {
                // Deserialize command data to the strongly-typed TCommand.
                var command = _serializer.Deserialize(
                    notification.CommandData,
                    descriptor.CommandType);

                // Execute inside a fresh DI scope so scoped services work correctly.
                using var scope = _scopeFactory.CreateScope();
                result = await descriptor.Execute(scope.ServiceProvider, command, stoppingToken)
                                         .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Command executor '{HandlerKey}' threw an exception. " +
                    "CommandWaitId={CommandWaitId}",
                    notification.HandlerKey, notification.CommandWaitId);
                // TODO: retry / dead-letter policy
                return;
            }

            // Post the result back to the Orchestrator.
            var resultDto = new CommandResultDto
            {
                CommandWaitId   = notification.CommandWaitId,
                Result          = result,
                ClientSentTime  = DateTime.UtcNow
            };

            try
            {
                await _dispatcher.DispatchAsync(resultDto).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to dispatch CommandResultDto back to the Orchestrator. " +
                    "CommandWaitId={CommandWaitId}",
                    notification.CommandWaitId);
            }
        }

        public override void Dispose()
        {
            _subscriber.Dispose();
            base.Dispose();
        }
    }
}
