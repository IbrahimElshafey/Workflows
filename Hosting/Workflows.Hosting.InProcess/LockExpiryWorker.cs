using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Abstraction.Persistence;

namespace Workflows.Hosting.InProcess
{
    /// <summary>
    /// Background service that periodically releases expired instance locks.
    /// This prevents permanent lockout when a node crashes or loses connectivity
    /// while holding a lock on a workflow instance.
    /// </summary>
    public class LockExpiryWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

        public LockExpiryWorker(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("[LOCK EXPIRY WORKER] Started. Sweeping expired locks every 30 seconds.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(SweepInterval, stoppingToken);

                    using var scope = _serviceProvider.CreateScope();
                    var lockManager = scope.ServiceProvider.GetService<IInstanceLockManager>();

                    if (lockManager != null)
                    {
                        var expiredCount = await lockManager.GetExpiredLocksAsync(stoppingToken);
                        if (expiredCount.Count > 0)
                        {
                            Console.WriteLine(
                                $"[LOCK EXPIRY WORKER] Found {expiredCount.Count} expired lock(s). Releasing...");
                            await lockManager.ReleaseExpiredLocksAsync(stoppingToken);
                            Console.WriteLine(
                                $"[LOCK EXPIRY WORKER] Released {expiredCount.Count} expired lock(s).");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LOCK EXPIRY WORKER] Error during sweep: {ex.Message}");
                }
            }

            Console.WriteLine("[LOCK EXPIRY WORKER] Stopped.");
        }
    }
}