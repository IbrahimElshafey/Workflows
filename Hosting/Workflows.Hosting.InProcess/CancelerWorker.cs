using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;
using Workflows.Storage.EntityFrameworkCore;
using Workflows.Runner;
using Workflows.Runner.Pipeline;

namespace Workflows.Hosting.InProcess
{
    public class CancelerWorker : BackgroundService
    {
        private readonly BackgroundWorkerChannel _channel;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _sweepInterval = TimeSpan.FromSeconds(1);

        public CancelerWorker(BackgroundWorkerChannel channel, IServiceProvider serviceProvider)
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
            var reader = _channel.CancellationReader;
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                while (reader.TryRead(out var request))
                {
                    try
                    {
                        await ProcessCancellationAsync(request.WorkflowInstanceId, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CANCELLER WORKER CHANNEL ERROR]: {ex}");
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
                    Console.WriteLine($"[CANCELLER WORKER SWEEPER ERROR]: {ex}");
                }

                await Task.Delay(_sweepInterval, stoppingToken);
            }
        }

        private async Task SweepDatabaseAsync(CancellationToken stoppingToken)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();

                // Find running instances that have cancellation entries in history
                var runningInstances = await dbContext.WorkflowInstances
                    .Where(i => i.Status == (int)WorkflowInstanceStatus.Running)
                    .ToListAsync(stoppingToken);

                var instances = runningInstances
                    .Where(i => i.CancellationHistory != null && i.CancellationHistory.Count > 0)
                    .Select(i => i.Id)
                    .ToList();

                foreach (var instanceId in instances)
                {
                    await ProcessCancellationAsync(instanceId, stoppingToken);
                }
            }
        }

        private async Task ProcessCancellationAsync(Guid instanceId, CancellationToken stoppingToken)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
                var callbackRegistry = scope.ServiceProvider.GetRequiredService<ICallbackRegistry>();
                var invokerCache = scope.ServiceProvider.GetRequiredService<ActionInvokerCache>();

                var state = await store.GetInstanceStateAsync(instanceId);
                if (state == null || state.Status != WorkflowInstanceStatus.Running) return;

                var cancelledTokens = state.CancellationHistory.GetCancelledTokens();
                if (cancelledTokens.Count == 0) return;

                var completedWaitIds = new List<string>();
                bool updated = false;

                // Recursively scan and cancel waits
                updated = await CancelWaitsRecursive(state.Waits, cancelledTokens, completedWaitIds, callbackRegistry, invokerCache, state);

                if (updated)
                {
                    await store.SaveContextSyncAsync(state, completedWaitIds);
                }
            }
        }

        private async Task<bool> CancelWaitsRecursive(
            IEnumerable<WaitInfrastructureDto> waits, 
            HashSet<string> cancelledTokens, 
            List<string> completedWaitIds, 
            ICallbackRegistry callbackRegistry,
            ActionInvokerCache invokerCache,
            WorkflowStateDto state)
        {
            if (waits == null) return false;
            bool updated = false;

            foreach (var wait in waits)
            {
                if (wait.Status == WaitStatus.Waiting && wait.CancelTokens != null && wait.CancelTokens.Intersect(cancelledTokens).Any())
                {
                    wait.Status = WaitStatus.Canceled;
                    completedWaitIds.Add(wait.Id);
                    updated = true;

                    // Execute cancellation callback if registered
                    if (wait is CommandWaitDto cmd && !string.IsNullOrEmpty(cmd.CancelAction))
                    {
                        if (callbackRegistry.TryGet(cmd.CancelAction, out var callback) && callback != null)
                        {
                            var invoker = invokerCache.GetOrAddCancelActionInvoker(callback.GetType());
                            if (invoker != null)
                            {
                                await invoker(callback);
                            }
                        }
                    }
                    else if (wait is SignalWaitDto sig && !string.IsNullOrEmpty(sig.CancelAction))
                    {
                        if (callbackRegistry.TryGet(sig.CancelAction, out var callback) && callback != null)
                        {
                            var invoker = invokerCache.GetOrAddCancelActionInvoker(callback.GetType());
                            if (invoker != null)
                            {
                                await invoker(callback);
                            }
                        }
                    }
                    else if (wait is TimeWaitDto time && !string.IsNullOrEmpty(time.CancelAction))
                    {
                        if (callbackRegistry.TryGet(time.CancelAction, out var callback) && callback != null)
                        {
                            var invoker = invokerCache.GetOrAddCancelActionInvoker(callback.GetType());
                            if (invoker != null)
                            {
                                await invoker(callback);
                            }
                        }
                    }

                    // Cancel child waits recursively
                    CancelChildrenRecursive(wait.ChildWaits, completedWaitIds);
                }

                if (wait.ChildWaits != null && wait.ChildWaits.Count > 0)
                {
                    if (await CancelWaitsRecursive(wait.ChildWaits, cancelledTokens, completedWaitIds, callbackRegistry, invokerCache, state))
                    {
                        updated = true;
                    }
                }
            }

            return updated;
        }

        private void CancelChildrenRecursive(IEnumerable<WaitInfrastructureDto> children, List<string> completedWaitIds)
        {
            if (children == null) return;
            foreach (var child in children)
            {
                if (child.Status == WaitStatus.Waiting)
                {
                    child.Status = WaitStatus.Canceled;
                    completedWaitIds.Add(child.Id);
                    CancelChildrenRecursive(child.ChildWaits, completedWaitIds);
                }
            }
        }
    }
}
