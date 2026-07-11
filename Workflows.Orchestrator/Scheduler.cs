using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Persistence;

namespace Workflows.Orchestrator
{
    public class Scheduler : IExternalScheduler, IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<Guid, TimerRecord> _timers = new();
        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;

        public Scheduler(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public Task ScheduleSignalAsync(string signalIdentifier, object payload, DateTime executeAt)
        {
            var record = new TimerRecord
            {
                Id = Guid.NewGuid(),
                SignalIdentifier = signalIdentifier,
                Payload = payload,
                ExecuteAt = executeAt
            };
            _timers.TryAdd(record.Id, record);
            return Task.CompletedTask;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = new CancellationTokenSource();

            // Load pending timers on startup
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var store = scope.ServiceProvider.GetService<IWorkflowStore>();
                    if (store != null)
                    {
                        var pendingTimers = await store.GetPendingTimeWaitsAsync();
                        foreach (var timer in pendingTimers)
                        {
                            var record = new TimerRecord
                            {
                                Id = Guid.NewGuid(),
                                SignalIdentifier = timer.UniqueMatchId,
                                Payload = null!,
                                ExecuteAt = timer.ExecutionTime
                            };
                            _timers.TryAdd(record.Id, record);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fail-silent on startup errors (e.g. database not initialized yet in some integration tests)
            }

            _backgroundTask = RunTimerLoopAsync(_cts.Token);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_cts != null)
            {
                _cts.Cancel();
            }

            if (_backgroundTask != null)
            {
                try
                {
                    await _backgroundTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown
                }
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
                    var expiredTimers = _timers.Values
                        .Where(t => t.ExecuteAt <= now)
                        .ToList();

                    foreach (var timer in expiredTimers)
                    {
                        if (_timers.TryRemove(timer.Id, out _))
                        {
                            // Fire signal processing in a background Task to keep loop non-blocking
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    using (var scope = _serviceProvider.CreateScope())
                                    {
                                        var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
                                        await orchestrator.ProcessSignalAsync(new Workflows.Abstraction.DTOs.SignalDto
                                        {
                                            Id = Guid.NewGuid(),
                                            SignalIdentifier = timer.SignalIdentifier,
                                            Data = timer.Payload
                                        });
                                    }
                                }
                                catch (Exception)
                                {
                                    // Log or handle timer firing failure
                                }
                            }, CancellationToken.None);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Prevent background thread from dying
                }
            }
        }

        private class TimerRecord
        {
            public Guid Id { get; set; }
            public string SignalIdentifier { get; set; }
            public object Payload { get; set; }
            public DateTime ExecuteAt { get; set; }
        }
    }
}
