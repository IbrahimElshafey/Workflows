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
            IEnumerable<string> completedWaitIds,
            Guid? triggeringSignalId = null);

        Task<bool> HasSignalBeenProcessedAsync(Guid messageId);

        Task PruneProcessedSignalsAsync(DateTime threshold);


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

        /// <summary>
        /// Registers or updates the workflow capability map for a worker DLL release version.
        /// </summary>
        Task RegisterWorkerCapabilitiesAsync(string dllVersion, IEnumerable<(string workflowType, int workflowVersion)> capabilities, System.Threading.CancellationToken ct = default);

        /// <summary>
        /// Retrieves active DLL release versions that host a specific workflow type and version.
        /// </summary>
        Task<List<string>> GetActiveDllVersionsForWorkflowAsync(string workflowType, int workflowVersion, System.Threading.CancellationToken ct = default);

        /// <summary>
        /// Retrieves live active running instance counts grouped by DLL release version.
        /// </summary>
        Task<Dictionary<string, int>> GetActiveInstanceCountsByDllVersionAsync(System.Threading.CancellationToken ct = default);

        /// <summary>
        /// Atomically claims up to <paramref name="batchSize"/> due timers for an engine node, transitioning their status to Matched (200).
        /// </summary>
        Task<List<TimeWaitDto>> ClaimDueTimersAsync(int batchSize, string nodeId, System.Threading.CancellationToken ct = default);

        /// <summary>
        /// Resets Matched timers back to Waiting if an engine node crashed mid-execution.
        /// </summary>
        Task RecoverStaleTimersAsync(TimeSpan staleThreshold, System.Threading.CancellationToken ct = default);
    }
}
