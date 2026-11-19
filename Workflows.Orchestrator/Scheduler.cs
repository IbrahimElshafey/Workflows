using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Persistence;

namespace Workflows.Orchestrator
{
    /// <summary>
    /// Native Clustered Distributed Timer Engine.
    /// Combines in-memory fast-path scheduling for near-term timers (< 60s)
    /// with clustered DB poller claiming (using atomic status transition) for multi-engine nodes sharing the same database.
    /// </summary>
    public class Scheduler : IExternalScheduler, IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<Guid, TimerRecord> _timers = new();
        private readonly Channel<SignalDto> _dispatchChannel;
        private readonly string _nodeId;
        private CancellationTokenSource? _cts;
        private Task? _timerLoopTask;
        private Task? _consumerLoopTask;
        private DateTime _lastStaleRecoveryCheck = DateTime.MinValue;

        public string NodeId => _nodeId;

        public Scheduler(IServiceProvider serviceProvider, int channelCapacity = 1000)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _nodeId = $"{Environment.MachineName}_{Guid.NewGuid():N}";

            var options = new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            };
            _dispatchChannel = Channel.CreateBounded<SignalDto>(options);
        }

        public Task ScheduleSignalAsync(Guid timerId, string signalIdentifier, object payload, DateTime executeAt)
        {
            var record = new TimerRecord
            {
                Id = timerId,
                SignalIdentifier = signalIdentifier,
                Payload = payload,
                ExecuteAt = executeAt
            };
            _timers.TryAdd(record.Id, record);
            return Task.CompletedTask;
        }

        public Task CancelScheduledSignalAsync(Guid timerId)
        {
            _timers.TryRemove(timerId, out _);
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = new CancellationTokenSource();

            _consumerLoopTask = Task.Run(() => RunChannelConsumerLoopAsync(_cts.Token), CancellationToken.None);
            _timerLoopTask = Task.Run(() => RunTimerLoopAsync(_cts.Token), CancellationToken.None);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_cts != null)
            {
                _cts.Cancel();
            }

            _dispatchChannel.Writer.TryComplete();

            var tasks = new List<Task>();
            if (_timerLoopTask != null) tasks.Add(_timerLoopTask);
            if (_consumerLoopTask != null) tasks.Add(_consumerLoopTask);

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception)
            {
                // Ignore cancellation exceptions during shutdown
            }
        }

        private async Task RunTimerLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(100, cancellationToken);
                    var now = DateTime.UtcNow;

                    // 1. In-Memory Fast Path Execution
                    var expiredInMemory = _timers.Values
                        .Where(t => t.ExecuteAt <= now)
                        .ToList();

                    foreach (var timer in expiredInMemory)
                    {
                        if (_timers.TryRemove(timer.Id, out _))
                        {
                            await _dispatchChannel.Writer.WriteAsync(new SignalDto
                            {
                                Id = timer.Id,
                                SignalIdentifier = timer.SignalIdentifier,
                                Data = timer.Payload
                            }, cancellationToken);
                        }
                    }

                    // 2. Clustered Database Polling Path
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var store = scope.ServiceProvider.GetService<IWorkflowStore>();
                        if (store != null)
                        {
                            // Claim due timers atomically across parallel nodes
                            var claimed = await store.ClaimDueTimersAsync(50, _nodeId, cancellationToken);
                            foreach (var timer in claimed)
                            {
                                if (Guid.TryParse(timer.Id, out var timerGuid))
                                {
                                    // Remove from in-memory if present to avoid double processing
                                    _timers.TryRemove(timerGuid, out _);

                                    await _dispatchChannel.Writer.WriteAsync(new SignalDto
                                    {
                                        Id = timerGuid,
                                        SignalIdentifier = timer.UniqueMatchId,
                                        Data = null
                                    }, cancellationToken);
                                }
                            }

                            // 3. Stale Lock Recovery Sweep (every 30 seconds)
                            if ((now - _lastStaleRecoveryCheck).TotalSeconds >= 30)
                            {
                                _lastStaleRecoveryCheck = now;
                                await store.RecoverStaleTimersAsync(TimeSpan.FromMinutes(5), cancellationToken);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Fail-safe loop continuation
                }
            }
        }

        private async Task RunChannelConsumerLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var signalDto in _dispatchChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
                            await orchestrator.ProcessSignalAsync(signalDto);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception)
                    {
                        // Fail-safe dispatch catch
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation
            }
            catch (ObjectDisposedException)
            {
                // Expected on container shutdown
            }
        }

        private class TimerRecord
        {
            public Guid Id { get; set; }
            public string SignalIdentifier { get; set; } = string.Empty;
            public object? Payload { get; set; }
            public DateTime ExecuteAt { get; set; }
        }
    }
}
