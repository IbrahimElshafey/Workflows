using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Persistence;

namespace Workflows.Hosting.InProcess
{
    /// <summary>
    /// Tunables for <see cref="WorkerDrainWorker"/>.
    /// </summary>
    public class WorkerDrainOptions
    {
        /// <summary>
        /// How long a worker version must observe zero active instances before its process is terminated.
        /// The window absorbs the race where an instance is about to be routed to the old version right
        /// after the count was sampled.
        /// </summary>
        public TimeSpan DrainingGracePeriod { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Terminate a worker even when its version still owns parked instances
        /// (<c>CompensationFailed</c> / <c>MigrationFailed</c>). Off by default: those instances need the
        /// old worker (or a snapshot rollback) before they can make progress.
        /// </summary>
        public bool AllowShutdownWithParkedInstances { get; set; }

        /// <summary>
        /// Archive the worker build before terminating it, so a late compensation can respawn the exact
        /// version even after the host has moved to a newer runtime. On by default.
        /// </summary>
        public bool ArchiveBeforeShutdown { get; set; } = true;
    }

    public class WorkerDrainWorker : BackgroundService
    {
        private readonly WorkerProcessSupervisor _supervisor;
        private readonly IWorkflowStore _store;
        private readonly ILogger<WorkerDrainWorker>? _logger;
        private readonly TimeSpan _checkInterval;
        private readonly WorkerDrainOptions _options;

        /// <summary>When each worker version was first observed with zero active instances.</summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _zeroActiveSince = new();

        public WorkerDrainWorker(
            WorkerProcessSupervisor supervisor,
            IWorkflowStore store,
            ILogger<WorkerDrainWorker>? logger = null,
            TimeSpan? checkInterval = null,
            WorkerDrainOptions? options = null)
        {
            _supervisor = supervisor;
            _store = store;
            _logger = logger;
            _checkInterval = checkInterval ?? TimeSpan.FromSeconds(30);
            _options = options ?? new WorkerDrainOptions();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_checkInterval, stoppingToken);
                    await CheckAndDrainWorkersAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in WorkerDrainWorker background loop");
                }
            }
        }

        public async Task CheckAndDrainWorkersAsync(CancellationToken ct = default)
        {
            var activeCounts = await _store.GetActiveInstanceCountsByDllVersionAsync(ct);
            var parkedCounts = await _store.GetParkedInstanceCountsByDllVersionAsync(ct);
            var runningWorkers = _supervisor.ActiveWorkers.ToList();

            foreach (var kvp in runningWorkers)
            {
                string dllVersion = kvp.Key;

                // Unknown versions are not ours to terminate; a worker that has not registered
                // capabilities yet must not be dropped on the first sweep.
                if (!activeCounts.TryGetValue(dllVersion, out int activeCount) || activeCount > 0)
                {
                    _zeroActiveSince.TryRemove(dllVersion, out _);
                    continue;
                }

                if (parkedCounts.TryGetValue(dllVersion, out int parkedCount) && parkedCount > 0
                    && !_options.AllowShutdownWithParkedInstances)
                {
                    _logger?.LogWarning(
                        "DLL Version {DllVersion} has {ParkedCount} parked instance(s) (CompensationFailed/MigrationFailed). " +
                        "Keeping the worker alive; resolve them or restore a pre-migration snapshot from WorkflowInstanceSnapshots.",
                        dllVersion,
                        parkedCount);
                    continue;
                }

                var now = DateTime.UtcNow;
                if (!_zeroActiveSince.TryGetValue(dllVersion, out var zeroSince))
                {
                    _zeroActiveSince[dllVersion] = now;
                    _logger?.LogInformation(
                        "DLL Version {DllVersion} reached 0 active instances. Draining for {GracePeriod} before terminating.",
                        dllVersion,
                        _options.DrainingGracePeriod);
                    continue;
                }

                var elapsed = now - zeroSince;
                if (elapsed < _options.DrainingGracePeriod)
                {
                    _logger?.LogDebug(
                        "DLL Version {DllVersion} still inside its drain grace period ({Elapsed} of {GracePeriod}).",
                        dllVersion,
                        elapsed,
                        _options.DrainingGracePeriod);
                    continue;
                }

                _logger?.LogInformation(
                    "DLL Version {DllVersion} has had 0 active running workflow instances for {Elapsed}. Terminating worker sub-process.",
                    dllVersion,
                    elapsed);
                _zeroActiveSince.TryRemove(dllVersion, out _);

                if (_options.ArchiveBeforeShutdown)
                {
                    bool archived = await _supervisor.TryArchiveWorkerAsync(dllVersion, ct: ct);
                    if (archived)
                    {
                        _logger?.LogInformation(
                            "Archived the published build of DLL Version {DllVersion}; a late compensation can still respawn it.",
                            dllVersion);
                    }
                    else
                    {
                        _logger?.LogWarning(
                            "Could not archive DLL Version {DllVersion}. Parked instances needing it after the host upgrade will have to be restored from a snapshot.",
                            dllVersion);
                    }
                }

                await _supervisor.ShutdownWorkerAsync(dllVersion, ct);
            }
        }
    }
}