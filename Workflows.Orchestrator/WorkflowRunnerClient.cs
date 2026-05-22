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

namespace Workflows.Orchestrator
{
    public class WorkflowRunnerClient : IWorkflowRunnerClient
    {
        private readonly IWorkflowStore _workflowStore;
        private readonly IExternalScheduler _scheduler;

        public WorkflowRunnerClient(IWorkflowStore workflowStore, IExternalScheduler scheduler)
        {
            _workflowStore = workflowStore ?? throw new ArgumentNullException(nameof(workflowStore));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        public async Task<AsyncResult> SendWorkflowRunResultAsync(
            AsyncResult runResult,
            WorkflowExecutionResponse result,
            CancellationToken cancellationToken = default)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            // Commit the state updates, new/active waits, and completed wait IDs atomically
            await _workflowStore.SaveContextSyncAsync(
                result.UpdatedState,
                result.UpdatedState.Waits,
                result.ConsumedWaitsIds);

            // Scan the active waits recursively for any waiting TimeWaitDto and schedule them
            var timeWaits = new List<TimeWaitDto>();
            foreach (var wait in result.UpdatedState.Waits)
            {
                CollectTimeWaits(wait, timeWaits);
            }

            foreach (var timeWait in timeWaits)
            {
                if (timeWait.Status == Abstraction.Enums.WaitStatus.Waiting)
                {
                    var executeAt = DateTime.UtcNow.Add(timeWait.TimeToWait);
                    await _scheduler.ScheduleSignalAsync(timeWait.UniqueMatchId, null, executeAt);
                }
            }

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
    }
}
