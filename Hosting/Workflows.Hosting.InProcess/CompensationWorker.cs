using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;
using Workflows.Storage.EntityFrameworkCore;
using Workflows.Runner;
using Workflows.Runner.Pipeline;

namespace Workflows.Hosting.InProcess
{
    public class CompensationWorker : BackgroundService
    {
        private readonly BackgroundWorkerChannel _channel;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _sweepInterval = TimeSpan.FromSeconds(1);

        public CompensationWorker(BackgroundWorkerChannel channel, IServiceProvider serviceProvider)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var channelTask = ConsumeChannelAsync(stoppingToken);
            var pollTask = SweepPeriodicallyAsync(stoppingToken);

            await Task.WhenAll(channelTask, pollTask);
        }

        private async Task ConsumeChannelAsync(CancellationToken stoppingToken)
        {
            var reader = _channel.CompensationReader;
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                while (reader.TryRead(out var request))
                {
                    try
                    {
                        await ProcessCompensationAsync(request.WorkflowInstanceId, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[COMPENSATION WORKER CHANNEL ERROR]: {ex}");
                    }
                }
            }
        }

        private async Task SweepPeriodicallyAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepDatabaseAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[COMPENSATION WORKER SWEEPER ERROR]: {ex}");
                }

                await Task.Delay(_sweepInterval, stoppingToken);
            }
        }

        private async Task SweepDatabaseAsync(CancellationToken stoppingToken)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
                
                // Find running or failed instances that have active CompensationWait records in DB
                var failedInstances = await dbContext.WorkflowInstances
                    .Where(i => i.Status == (int)WorkflowInstanceStatus.InError)
                    .Select(i => i.Id)
                    .ToListAsync(stoppingToken);

                foreach (var instanceId in failedInstances)
                {
                    await ProcessCompensationAsync(instanceId, stoppingToken);
                }
            }
        }

        private async Task ProcessCompensationAsync(Guid instanceId, CancellationToken stoppingToken)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
                var registry = scope.ServiceProvider.GetRequiredService<IWorkflowRegistry>();
                var hydrator = scope.ServiceProvider.GetRequiredService<IWorkflowHydrator>();
                var callbackRegistry = scope.ServiceProvider.GetRequiredService<ICallbackRegistry>();
                var stateService = scope.ServiceProvider.GetRequiredService<WorkflowStateService>();

                var state = await store.GetInstanceStateAsync(instanceId);
                if (state == null || state.Status != WorkflowInstanceStatus.InError) return;

                // Find active compensation waits
                var activeCompWaits = new List<CompensationWaitDto>();
                CollectActiveCompensationWaits(state.Waits, activeCompWaits);

                if (activeCompWaits.Count == 0) return;

                // Load container and state parameters
                if (!registry.Workflows.TryGetValue(state.WorkflowType, out var workflowTypes))
                {
                    throw new InvalidOperationException($"Workflow {state.WorkflowType} not registered.");
                }

                var containerInstance = hydrator.CreateInstance(workflowTypes.WorkflowContainer);
                state.StateObject.Instance = containerInstance;

                var stateType = workflowTypes.StateType;
                if (!state.StateObject.Locals.TryGetValue("state", out var stateObj) || stateObj == null)
                {
                    if (stateType != typeof(object))
                    {
                        stateObj = Activator.CreateInstance(stateType);
                        state.StateObject.Locals["state"] = stateObj;
                    }
                }

                var completedWaitIds = new List<string>();

                foreach (var compWait in activeCompWaits)
                {
                    // Find all completed command waits that have matching compensation tokens
                    var matchingCommands = new List<CommandWaitDto>();
                    CollectCompletedCommandsWithToken(state.Waits, compWait.Token, matchingCommands);

                    // Execute compensation in LIFO order (reverse of execution order)
                    matchingCommands.Reverse();

                    var invokerCache = scope.ServiceProvider.GetRequiredService<ActionInvokerCache>();

                    foreach (var cmd in matchingCommands)
                    {
                        var key = cmd.HandlerKey + ":Compensation";
                        if (callbackRegistry.TryGet(key, out var callback) && callback != null)
                        {
                            var invoker = invokerCache.GetOrAddCompensationInvoker(callback.GetType());
                            if (invoker != null)
                            {
                                object? resultPayload = cmd.CommandResult;
                                if (resultPayload is string jsonStr && !string.IsNullOrEmpty(jsonStr))
                                {
                                    // Resolve expected result type
                                    if (registry.CommandTypes.TryGetValue(cmd.HandlerKey, out var cmdTypes))
                                    {
                                        var serializer = scope.ServiceProvider.GetRequiredService<IObjectSerializer>();
                                        resultPayload = serializer.Deserialize(jsonStr, cmdTypes.CommandResultType);
                                    }
                                }

                                await invoker(callback, resultPayload!, stateObj!);
                            }
                        }
                    }

                    // Complete this compensation wait
                    compWait.Status = WaitStatus.Completed;
                    completedWaitIds.Add(compWait.Id);
                }

                // Save updated state
                await store.SaveContextSyncAsync(state, completedWaitIds);
            }
        }

        private void CollectActiveCompensationWaits(IEnumerable<WaitInfrastructureDto> waits, List<CompensationWaitDto> result)
        {
            if (waits == null) return;
            foreach (var wait in waits)
            {
                if (wait is CompensationWaitDto compWait && compWait.Status == WaitStatus.Waiting)
                {
                    result.Add(compWait);
                }
                if (wait.ChildWaits != null)
                {
                    CollectActiveCompensationWaits(wait.ChildWaits, result);
                }
            }
        }

        private void CollectCompletedCommandsWithToken(IEnumerable<WaitInfrastructureDto> waits, string token, List<CommandWaitDto> result)
        {
            if (waits == null) return;
            foreach (var wait in waits)
            {
                if (wait is CommandWaitDto cmd && 
                    cmd.Status == WaitStatus.Completed && 
                    cmd.CompensationTokens != null && 
                    cmd.CompensationTokens.Contains(token))
                {
                    result.Add(cmd);
                }
                if (wait.ChildWaits != null)
                {
                    CollectCompletedCommandsWithToken(wait.ChildWaits, token, result);
                }
            }
        }
    }
}
