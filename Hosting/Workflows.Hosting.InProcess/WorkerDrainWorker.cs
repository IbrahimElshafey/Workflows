using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Persistence;

namespace Workflows.Hosting.InProcess
{
    public class WorkerDrainWorker : BackgroundService
    {
        private readonly WorkerProcessSupervisor _supervisor;
        private readonly IWorkflowStore _store;
        private readonly ILogger<WorkerDrainWorker>? _logger;
        private readonly TimeSpan _checkInterval;

        public WorkerDrainWorker(WorkerProcessSupervisor supervisor, IWorkflowStore store, ILogger<WorkerDrainWorker>? logger = null, TimeSpan? checkInterval = null)
        {
            _supervisor = supervisor;
            _store = store;
            _logger = logger;
            _checkInterval = checkInterval ?? TimeSpan.FromSeconds(30);
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
            var runningWorkers = _supervisor.ActiveWorkers.ToList();

            foreach (var kvp in runningWorkers)
            {
                string dllVersion = kvp.Key;
                if (activeCounts.TryGetValue(dllVersion, out int activeCount) && activeCount == 0)
                {
                    _logger?.LogInformation("DLL Version {DllVersion} has 0 active running workflow instances. Terminating worker sub-process.", dllVersion);
                    await _supervisor.ShutdownWorkerAsync(dllVersion, ct);
                }
            }
        }
    }
}

