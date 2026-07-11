using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;
using Workflows.Communication.Abstraction;

namespace Workflows.Orchestrator
{
    public class WorkflowRunnerClient : IWorkflowRunnerClient
    {
        private readonly IWorkflowStore _workflowStore;
        private readonly IExternalScheduler _scheduler;
        private readonly IMessageDispatcher _dispatcher;

        public WorkflowRunnerClient(
            IWorkflowStore workflowStore, 
            IExternalScheduler scheduler,
            IMessageDispatcher dispatcher)
        {
            _workflowStore = workflowStore ?? throw new ArgumentNullException(nameof(workflowStore));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public async Task<AsyncResult> SendWorkflowRunResultAsync(
            AsyncResult runResult,
            WorkflowExecutionResponse result,
            CancellationToken cancellationToken = default)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            // 1. Get existing waits to identify which ones are new
            var existingState = await _workflowStore.GetInstanceStateAsync(result.UpdatedState.Id);
            var existingWaitIds = existingState != null
                ? existingState.Waits.Select(w => w.Id).ToHashSet()
                : new HashSet<string>();

            // 2. Commit the state updates, new/active waits, and completed wait IDs atomically
            await _workflowStore.SaveContextSyncAsync(
                result.UpdatedState,
                result.ConsumedWaitsIds,
                result.TriggeringSignalId);


            // 3. Scan the active waits recursively for any waiting TimeWaitDto and schedule them
            var activeWaits = result.UpdatedState.Waits ?? new List<WaitInfrastructureDto>();

            var timeWaits = new List<TimeWaitDto>();
            foreach (var wait in activeWaits)
            {
                CollectTimeWaits(wait, timeWaits);
            }

            foreach (var timeWait in timeWaits)
            {
                // Skip already-existing waits (not newly added this run)
                if (existingWaitIds.Contains(timeWait.Id))
                    continue;

                if (timeWait.Status == Abstraction.Enums.WaitStatus.Waiting)
                {
                    // The wait Id uses a hierarchical format (e.g. "2/1/<instanceId>") which is not a GUID.
                    // UniqueMatchId is always a proper GUID used as the timer correlation key.
                    if (Guid.TryParse(timeWait.UniqueMatchId, out var timerGuid))
                    {
                        await _scheduler.ScheduleSignalAsync(timerGuid, timeWait.UniqueMatchId, null, timeWait.ExecutionTime);
                    }
                }
            }

            // Cancel any scheduled timers for waits that were consumed in this run
            if (result.ConsumedWaitsIds != null && result.ConsumedWaitsIds.Any())
            {
                // Collect ALL TimeWaits from the result state (includes already-completed ones) and the consumed ids
                // We need to cancel by UniqueMatchId for consumed TimeWaits
                var allTimeWaits = new List<TimeWaitDto>();
                foreach (var wait in activeWaits)
                    CollectTimeWaits(wait, allTimeWaits);

                // Also look in the existing state for consumed time waits
                if (existingState != null)
                {
                    foreach (var wait in existingState.Waits ?? new List<WaitInfrastructureDto>())
                        CollectTimeWaits(wait, allTimeWaits);
                }

                var timeWaitById = allTimeWaits
                    .Where(t => t.UniqueMatchId != null)
                    .GroupBy(t => t.Id)
                    .ToDictionary(g => g.Key, g => g.First().UniqueMatchId);

                foreach (var id in result.ConsumedWaitsIds)
                {
                    if (timeWaitById.TryGetValue(id, out var uniqueMatchId) &&
                        Guid.TryParse(uniqueMatchId, out var timerGuid))
                    {
                        await _scheduler.CancelScheduledSignalAsync(timerGuid);
                    }
                }
            }




            // 4. Deferred commands are now written to OutboxMessages table atomically in WorkflowStore.SaveContextSyncAsync.
            // The OutboxSweeperWorker is responsible for sweeping and dispatching them.

            return runResult;
        }

        private void CollectTimeWaits(WaitInfrastructureDto wait, List<TimeWaitDto> list)
        {
            if (wait == null) return;
            if (wait is TimeWaitDto timeWait)
            {
                list.Add(timeWait);
            }

            if (wait.ChildWaits != null)
            {
                foreach (var child in wait.ChildWaits)
                {
                    CollectTimeWaits(child, list);
                }
            }
        }

        private void CollectCommandWaits(WaitInfrastructureDto wait, List<CommandWaitDto> list)
        {
            if (wait == null) return;
            if (wait is CommandWaitDto commandWait)
            {
                list.Add(commandWait);
            }

            if (wait.ChildWaits != null)
            {
                foreach (var child in wait.ChildWaits)
                {
                    CollectCommandWaits(child, list);
                }
            }
        }
    }
}
