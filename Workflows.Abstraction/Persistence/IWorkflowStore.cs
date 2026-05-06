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
            IEnumerable<WaitInfrastructureDto> newWaits,
            IEnumerable<Guid> completedWaitIds);

        /// <summary>
        /// Retrieves the "Source of Truth" JSON document.
        /// </summary>
        Task<WorkflowStateDto> GetInstanceStateAsync(Guid instanceId);

        /// <summary>
        /// Fast relational lookup to find which instances are waiting for a specific signal path.
        /// </summary>
        Task<List<Guid>> FindInstancesWaitingForSignalAsync(string signalPath);

        /// <summary>
        /// Fast relational lookup for command completion.
        /// </summary>
        Task<Guid> GetInstanceByCommandWaitIdAsync(Guid commandWaitId);
    }

}

