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
        /// <para>
        /// When <paramref name="dlqMessage"/> is supplied the dead letter row is inserted inside the same
        /// transaction, so the terminal status change and the DLQ entry are committed together (or not at all).
        /// </para>
        /// </summary>
        Task SaveContextSyncAsync(
            WorkflowStateDto state,
            IEnumerable<string> completedWaitIds,
            Guid? triggeringSignalId = null,
            DlqMessage? dlqMessage = null);

        Task<bool> HasSignalBeenProcessedAsync(Guid messageId);

        Task PruneProcessedSignalsAsync(DateTime threshold);


        /// <summary>
        /// Retrieves the "Source of Truth" JSON document.
        /// </summary>
        Task<WorkflowStateDto> GetInstanceStateAsync(Guid instanceId);

        /// <summary>
        /// Bulk variant of <see cref="GetInstanceStateAsync"/> for signal fan-out: loads every requested
        /// instance with a single query instead of one round trip per candidate. Instances that do not
        /// exist are omitted from the result (the order of the result is not significant).
        /// </summary>
        Task<List<WorkflowStateDto>> GetInstanceStatesAsync(System.Collections.Generic.IReadOnlyCollection<Guid> instanceIds);

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
        /// <summary>
        /// Counts instances that a DLL version must stay alive for, keyed by DLL version. An instance
        /// counts as active while it is Running/InProgress/New, or while it is InError with a
        /// compensation wait still pending — the compensation handler lives in the same DLL version.
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

        /// <summary>
        /// Appends a dead letter queue entry for an instance whose compensation (saga rollback)
        /// exhausted its retries. The row is written before any <see cref="IDlqPublisher"/> is invoked.
        /// </summary>
        Task AppendDlqMessageAsync(DlqMessage message, System.Threading.CancellationToken ct = default);

        /// <summary>
        /// Writes the pre-migration snapshot of an instance. Called before a state migration touches any
        /// row so a failed migration can be diffed against - or rolled back to - the exact state it
        /// started from.
        /// </summary>
        Task SaveInstanceSnapshotAsync(WorkflowInstanceSnapshot snapshot, System.Threading.CancellationToken ct = default);

        /// <summary>
        /// Returns every snapshot recorded for an instance, oldest first.
        /// </summary>
        Task<List<WorkflowInstanceSnapshot>> GetInstanceSnapshotsAsync(Guid instanceId, System.Threading.CancellationToken ct = default);

        /// <summary>
        /// Rewrites an instance from a stored pre-migration snapshot. Returns false when the snapshot does
        /// not exist. Used as the rollback path for a failed migration.
        /// </summary>
        Task<bool> RestoreInstanceSnapshotAsync(string snapshotId, System.Threading.CancellationToken ct = default);

        /// <summary>
        /// Counts instances per DLL version that need operator attention before the version can be retired
        /// (<c>CompensationFailed</c> and <c>MigrationFailed</c>). Drain uses this to avoid terminating a
        /// worker that is still the only thing able to make progress on them.
        /// </summary>
        Task<Dictionary<string, int>> GetParkedInstanceCountsByDllVersionAsync(System.Threading.CancellationToken ct = default);

        /// <summary>
        /// Resets a <c>CompensationFailed</c> instance for a manual retry: compensation waits are moved back to
        /// <c>Waiting</c> with cleared retry state and the instance returns to <c>InError</c> so the compensation
        /// worker picks it up again. Idempotent - returns <c>false</c> when the instance is not stuck mid-rollback.
        /// </summary>
        Task<bool> ResetCompensationAsync(Guid instanceId, System.Threading.CancellationToken ct = default);
    }
}
