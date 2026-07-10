using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Abstraction.Persistence
{
    public interface IWorkflowStore
    {
        /// <summary>
        /// ATOMIC OPERATION: Updates the JSON StateObject and syncs the routing tables.
        /// This implementation MUST use a database transaction.
        /// </summary>
        Task SaveContextSyncAsync(
            WorkflowStateDto state,
            IEnumerable<string> completedWaitIds);

        /// <summary>
        /// Retrieves the "Source of Truth" JSON document.
        /// </summary>
        Task<WorkflowStateDto> GetInstanceStateAsync(Guid instanceId);

        /// <summary>
        /// Fast relational lookup to find which instances are waiting for a specific signal path and exact match data.
        /// </summary>
        Task<List<Guid>> FindInstancesWaitingForSignalAsync(string signalPath, string signalDataJson);

        /// <summary>
        /// Fast relational lookup for command completion.
        /// </summary>
        Task<Guid> GetInstanceByCommandWaitIdAsync(string commandWaitId);

        /// <summary>
        /// Retrieves all pending time waits from the database.
        /// </summary>
        Task<List<TimeWaitDto>> GetPendingTimeWaitsAsync();

        /// <summary>
        /// Atomically updates a workflow instance with its migrated version state and waits.
        /// </summary>
        Task ReplaceMigratedStateAsync(
            Guid instanceId,
            WorkflowStateDto newState,
            List<WaitInfrastructureDto> newWaits,
            int newVersion,
            System.Threading.CancellationToken ct);
    }
}
