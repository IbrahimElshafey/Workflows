using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;
using Workflows.Communication.Abstraction;
using Workflows.Storage.EntityFrameworkCore;

namespace Workflows.Hosting.InProcess
{
    public class OutboxSweeperWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _sweepInterval = TimeSpan.FromSeconds(1);

        private static readonly ConcurrentDictionary<Type, Func<object, object, Guid, Guid, CancellationToken, Task>> _dispatcherCache = new();

        public OutboxSweeperWorker(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepOutboxAsync(stoppingToken);
                }
                catch (Exception)
                {
                    // Fail-silent logging/retry on background errors
                }

                await Task.Delay(_sweepInterval, stoppingToken);
            }
        }

        private async Task SweepOutboxAsync(CancellationToken stoppingToken)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IMessageDispatcher>();
                var registryOptions = scope.ServiceProvider.GetService<CommandRegistryOptions>();

                var pendingMessages = await dbContext.OutboxMessages
                    .Where(m => m.Status == 0) // 0 = Pending
                    .OrderBy(m => m.CreatedAt)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                if (pendingMessages.Count == 0) return;

                foreach (var message in pendingMessages)
                {
                    try
                    {
                        var type = Type.GetType(message.MessageType);
                        if (type != null)
                        {
                            var deserialized = JsonConvert.DeserializeObject(message.Payload, type);
                            if (deserialized is CommandDispatchNotification notification && registryOptions != null)
                            {
                                var metadata = registryOptions.Commands.GetValueOrDefault(notification.HandlerKey);
                                if (metadata is { IsAsync: true })
                                {
                                    var dispatcherType = typeof(IDispatcher<>).MakeGenericType(metadata.InputType);
                                    var keyedDispatcher = scope.ServiceProvider.GetKeyedService(dispatcherType, notification.HandlerKey);
                                    if (keyedDispatcher != null)
                                    {
                                        object? commandInput = null;
                                        if (notification.CommandData is string jsonStr)
                                        {
                                            commandInput = JsonConvert.DeserializeObject(jsonStr, metadata.InputType);
                                        }
                                        else if (notification.CommandData != null)
                                        {
                                            if (metadata.InputType.IsInstanceOfType(notification.CommandData))
                                            {
                                                commandInput = notification.CommandData;
                                            }
                                            else
                                            {
                                                var json = JsonConvert.SerializeObject(notification.CommandData);
                                                commandInput = JsonConvert.DeserializeObject(json, metadata.InputType);
                                            }
                                        }

                                        if (commandInput != null)
                                        {
                                            var invoker = GetOrAddDispatcherInvoker(metadata.InputType);
                                            await invoker(keyedDispatcher, commandInput, notification.CommandWaitId, message.WorkflowInstanceId, stoppingToken);

                                            message.Status = 1; // Sent
                                            message.ProcessedAt = DateTime.UtcNow;
                                            continue;
                                        }
                                    }
                                }
                            }

                            if (deserialized != null)
                            {
                                // Fallback to legacy dynamic message dispatcher
                                await dispatcher.DispatchAsync((dynamic)deserialized);
                            }
                        }

                        message.Status = 1; // Sent
                        message.ProcessedAt = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[OUTBOX SWEEPER ERROR]: {ex}");
                        message.Status = 2; // Failed
                        message.ProcessedAt = DateTime.UtcNow;
                    }
                }

                await dbContext.SaveChangesAsync(stoppingToken);
            }
        }

        private static Func<object, object, Guid, Guid, CancellationToken, Task> GetOrAddDispatcherInvoker(Type inputType)
        {
            var dispatcherType = typeof(IDispatcher<>).MakeGenericType(inputType);
            return _dispatcherCache.GetOrAdd(dispatcherType, type =>
            {
                var dispatcherParam = Expression.Parameter(typeof(object), "dispatcher");
                var commandParam = Expression.Parameter(typeof(object), "command");
                var commandIdParam = Expression.Parameter(typeof(Guid), "commandId");
                var instanceIdParam = Expression.Parameter(typeof(Guid), "instanceId");
                var tokenParam = Expression.Parameter(typeof(CancellationToken), "token");

                var castDispatcher = Expression.Convert(dispatcherParam, type);
                var castCommand = Expression.Convert(commandParam, inputType);

                var dispatchMethod = type.GetMethod("DispatchAsync", new[] { inputType, typeof(Guid), typeof(Guid), typeof(CancellationToken) });
                var callExpr = Expression.Call(castDispatcher, dispatchMethod!, castCommand, commandIdParam, instanceIdParam, tokenParam);

                var convertMethod = typeof(OutboxSweeperWorker).GetMethod(nameof(ConvertValueTaskToTask), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var convertCall = Expression.Call(convertMethod!, callExpr);

                var lambda = Expression.Lambda<Func<object, object, Guid, Guid, CancellationToken, Task>>(
                    convertCall, dispatcherParam, commandParam, commandIdParam, instanceIdParam, tokenParam);

                return lambda.Compile();
            });
        }

        private static Task ConvertValueTaskToTask(ValueTask valueTask)
        {
            return valueTask.AsTask();
        }
    }
}
