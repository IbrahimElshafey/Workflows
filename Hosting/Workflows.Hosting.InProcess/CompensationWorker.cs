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
    /// <summary>
    /// Executes saga compensations for workflows that failed with <see cref="WorkflowInstanceStatus.InError"/>.
    /// <para>
    /// Compensation callbacks run in LIFO order. Each compensation wait keeps its own retry state
    /// (<see cref="CompensationWaitDto.Attempts"/>, <see cref="CompensationWaitDto.LastError"/>,
    /// <see cref="CompensationWaitDto.NextAttemptUtc"/>) which is persisted with the instance, so retries and
    /// backoff survive process restarts.
    /// </para>
    /// <para>
    /// Terminal states: when every compensation wait completes the instance becomes
    /// <see cref="WorkflowInstanceStatus.Canceled"/> (rollback succeeded, no DLQ entry). When a wait exhausts
    /// its attempts the instance becomes <see cref="WorkflowInstanceStatus.CompensationFailed"/> (rollback
    /// stopped mid-way) and a single <see cref="DlqMessage"/> is persisted and published.
    /// </para>
    /// </summary>
    public class CompensationWorker : BackgroundService
    {
        private readonly BackgroundWorkerChannel _channel;
        private readonly IServiceProvider _serviceProvider;
        private readonly CompensationWorkerOptions _options;
        private readonly ActionInvokerCache _invokerCache = new();

        public CompensationWorker(BackgroundWorkerChannel channel, IServiceProvider serviceProvider)
            : this(channel, serviceProvider, new CompensationWorkerOptions())
        {
        }

        public CompensationWorker(
            BackgroundWorkerChannel channel,
            IServiceProvider serviceProvider,
            CompensationWorkerOptions? options)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _options = options ?? new CompensationWorkerOptions();
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

                await Task.Delay(_options.EffectiveSweepInterval, stoppingToken);
            }
        }

        private async Task SweepDatabaseAsync(CancellationToken stoppingToken)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();

                // Only InError instances are eligible. CompensationFailed instances are parked until an
                // operator explicitly retries them (stuck rollback), so the sweeper must not touch them.
                var failedInstances = await dbContext.WorkflowInstances
                    .Where(i => i.Status == (int)WorkflowInstanceStatus.InError)
                    .Select(i => i.Id)
                    .ToListAsync(stoppingToken);

                foreach (var instanceId in failedInstances)
                {
                    try
                    {
                        await ProcessCompensationAsync(instanceId, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[COMPENSATION WORKER INSTANCE ERROR] instance={instanceId}: {ex}");
                    }
                }
            }
        }

        /// <summary>
        /// Runs one compensation cycle for the given instance. Safe to call repeatedly: it is a no-op
        /// unless the instance is <see cref="WorkflowInstanceStatus.InError"/> and has compensation waits
        /// that are due (no pending backoff).
        /// </summary>
        public async Task ProcessCompensationAsync(Guid instanceId, CancellationToken cancellationToken = default)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();

                var state = await store.GetInstanceStateAsync(instanceId);
                if (state == null || state.Status != WorkflowInstanceStatus.InError) return;

                var allCompWaits = new List<CompensationWaitDto>();
                CollectCompensationWaits(state.Waits, allCompWaits);

                if (allCompWaits.Count == 0) return;

                var completedWaitIds = new List<string>();
                var now = DateTime.UtcNow;

                var dueWaits = allCompWaits
                    .Where(w => w.Status == WaitStatus.Waiting)
                    .Where(w => w.NextAttemptUtc == null || w.NextAttemptUtc <= now)
                    .ToList();

                if (dueWaits.Count > 0)
                {
                    await ExecuteCompensationsAsync(scope.ServiceProvider, state, allCompWaits, dueWaits, completedWaitIds, cancellationToken);
                }

                var exhausted = allCompWaits.Where(w => w.Status == WaitStatus.InError).ToList();
                var stillWaiting = allCompWaits.Any(w => w.Status == WaitStatus.Waiting);

                if (exhausted.Count > 0)
                {
                    // Rollback stopped mid-way: park the instance so an operator can inspect and retry.
                    state.Status = WorkflowInstanceStatus.CompensationFailed;
                    state.ErrorMessage = BuildExhaustionMessage(exhausted);

                    // The status change and the dead letter row are committed by the same transaction, so a
                    // stuck rollback can never be persisted without its DLQ entry.
                    var dlqMessage = BuildDlqMessage(state, exhausted);
                    await store.SaveContextSyncAsync(state, completedWaitIds, null, dlqMessage);

                    // Publishing happens after the commit and is intentionally best-effort: the persisted row
                    // is the source of truth, and a crash here loses the publish but not the record.
                    await PublishDlqAsync(scope.ServiceProvider, dlqMessage);
                    return;
                }

                if (!stillWaiting)
                {
                    // Rollback finished successfully. Canceled is the engine terminal state for a
                    // rolled-back instance; no DLQ entry is emitted.
                    state.Status = WorkflowInstanceStatus.Canceled;
                    state.ErrorMessage = null;

                    await store.SaveContextSyncAsync(state, completedWaitIds);
                    return;
                }

                if (dueWaits.Count > 0)
                {
                    // Partial progress or a scheduled retry: persist attempt counters and NextAttemptUtc.
                    await store.SaveContextSyncAsync(state, completedWaitIds);
                }
            }
        }

        private async Task ExecuteCompensationsAsync(
            IServiceProvider scopeProvider,
            WorkflowStateDto state,
            List<CompensationWaitDto> allCompWaits,
            List<CompensationWaitDto> dueWaits,
            List<string> completedWaitIds,
            CancellationToken cancellationToken)
        {
            var registry = scopeProvider.GetRequiredService<IWorkflowRegistry>();
            var hydrator = scopeProvider.GetRequiredService<IWorkflowHydrator>();
            var callbackRegistry = scopeProvider.GetRequiredService<ICallbackRegistry>();
            var serializer = scopeProvider.GetRequiredService<IObjectSerializer>();

            if (!registry.Workflows.TryGetValue(state.WorkflowType!, out var workflowTypes))
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

            foreach (var compWait in dueWaits)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var matchingCommands = new List<CommandWaitDto>();
                CollectCompletedCommandsWithToken(state.Waits, compWait.Token, matchingCommands);

                // LIFO: undo in the reverse order of execution.
                matchingCommands.Reverse();

                try
                {
                    var missingHandlers = new List<string>();

                    foreach (var cmd in matchingCommands)
                    {
                        var key = cmd.HandlerKey + ":Compensation";
                        if (!callbackRegistry.TryGet(key, out var callback) || callback == null)
                        {
                            missingHandlers.Add(cmd.HandlerKey);
                            continue;
                        }

                        var invoker = _invokerCache.GetOrAddCompensationInvoker(callback.GetType());
                        if (invoker == null)
                        {
                            missingHandlers.Add(cmd.HandlerKey);
                            continue;
                        }

                        object? resultPayload = cmd.CommandResult;
                        if (resultPayload is string jsonStr && !string.IsNullOrEmpty(jsonStr)
                            && registry.CommandTypes.TryGetValue(cmd.HandlerKey, out var cmdTypes))
                        {
                            resultPayload = serializer.Deserialize(jsonStr, cmdTypes.CommandResultType);
                        }

                        await invoker(callback, resultPayload!, stateObj!);
                    }

                    // A completed command whose compensation handler is not registered (worker restarted,
                    // version redeployed without the handler, callback never captured by the mapper) must not
                    // be reported as a successful rollback. Surfacing it as a failed attempt routes the
                    // instance to CompensationFailed instead of Canceled.
                    if (missingHandlers.Count > 0)
                    {
                        throw new InvalidOperationException(
                            "No compensation handler is registered for command handler(s): "
                            + string.Join(", ", missingHandlers.Distinct())
                            + ". The rollback cannot be treated as complete.");
                    }

                    compWait.Status = WaitStatus.Completed;
                    compWait.LastError = null;
                    compWait.NextAttemptUtc = null;
                    completedWaitIds.Add(compWait.Id);
                }
                catch (Exception ex)
                {
                    compWait.Attempts++;
                    compWait.LastError = ex.Message;

                    if (compWait.Attempts >= _options.EffectiveMaxAttempts)
                    {
                        compWait.Status = WaitStatus.InError;
                        compWait.NextAttemptUtc = null;
                        Console.WriteLine(
                            $"[COMPENSATION EXHAUSTED] instance={state.Id} wait={compWait.Id} attempts={compWait.Attempts}/{_options.EffectiveMaxAttempts}: {ex.Message}");
                    }
                    else
                    {
                        compWait.Status = WaitStatus.Waiting;
                        compWait.NextAttemptUtc = DateTime.UtcNow + ComputeBackoff(compWait.Attempts);
                        Console.WriteLine(
                            $"[COMPENSATION RETRY] instance={state.Id} wait={compWait.Id} attempt={compWait.Attempts}/{_options.EffectiveMaxAttempts} next={compWait.NextAttemptUtc:o}: {ex.Message}");
                    }

                    completedWaitIds.Add(compWait.Id);
                }
            }
        }

        private TimeSpan ComputeBackoff(int attempts)
        {
            var exponent = Math.Max(0, attempts - 1);
            var milliseconds = _options.EffectiveInitialDelay.TotalMilliseconds * Math.Pow(_options.EffectiveBackoffMultiplier, exponent);

            if (double.IsInfinity(milliseconds) || double.IsNaN(milliseconds)) return _options.EffectiveMaxDelay;

            var delay = TimeSpan.FromMilliseconds(milliseconds);
            return delay > _options.EffectiveMaxDelay ? _options.EffectiveMaxDelay : delay;
        }

        private static string BuildExhaustionMessage(IReadOnlyList<CompensationWaitDto> exhausted)
        {
            var details = string.Join("; ", exhausted.Select(w =>
                $"{w.Token ?? w.Id}: {w.LastError ?? "unknown error"} (attempts={w.Attempts})"));

            return $"Compensation failed after exhausting retries. {details}";
        }

        private static DlqMessage BuildDlqMessage(
            WorkflowStateDto state,
            IReadOnlyList<CompensationWaitDto> exhausted)
        {
            return new DlqMessage
            {
                InstanceId = state.Id,
                WorkflowType = state.WorkflowType ?? string.Empty,
                FailureCount = Math.Max(1, exhausted.Sum(w => w.Attempts)),
                PayloadJson = JsonConvert.SerializeObject(new
                {
                    instanceId = state.Id,
                    workflowType = state.WorkflowType,
                    workflowVersion = state.WorkflowVersion,
                    failedWaits = exhausted.Select(w => new
                    {
                        waitId = w.Id,
                        token = w.Token,
                        attempts = w.Attempts,
                        lastError = w.LastError
                    })
                }),
                ErrorMessage = state.ErrorMessage,
                FailedAt = DateTime.UtcNow
            };
        }

        private static async Task PublishDlqAsync(IServiceProvider scopeProvider, DlqMessage dlqMessage)
        {
            try
            {
                var publisher = scopeProvider.GetService<IDlqPublisher>();
                if (publisher != null)
                {
                    await publisher.PublishAsync(dlqMessage);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[COMPENSATION DLQ PUBLISH ERROR] instance={dlqMessage.InstanceId}: {ex}");
            }
        }

        private void CollectCompensationWaits(IEnumerable<WaitInfrastructureDto>? waits, List<CompensationWaitDto> result)
        {
            if (waits == null) return;
            foreach (var wait in waits)
            {
                if (wait is CompensationWaitDto compWait)
                {
                    result.Add(compWait);
                }
                CollectCompensationWaits(wait.ChildWaits, result);
            }
        }

        private void CollectCompletedCommandsWithToken(IEnumerable<WaitInfrastructureDto>? waits, string token, List<CommandWaitDto> result)
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
                CollectCompletedCommandsWithToken(wait.ChildWaits, token, result);
            }
        }
    }
}