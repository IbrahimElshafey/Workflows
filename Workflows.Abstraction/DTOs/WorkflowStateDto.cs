using System;
using System.Collections.Generic;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;

namespace Workflows.Abstraction.DTOs
{
    /// <summary>
    /// Represents the persistent state of a workflow instance.
    /// Contains serialized workflow data and the current wait tree.
    /// </summary>
    public class WorkflowStateDto
    {
        /// <summary>
        /// Unique identifier for this workflow state.
        /// </summary>
        public Guid Id { get; internal set; }

        /// <summary>
        /// UTC timestamp when this workflow state was created.
        /// </summary>
        public DateTime Created { get; internal set; }

        /// <summary>
        /// Serialized instance that contains the resumable workflow instance data and all locals (closures,methods private data) needed for execustion.
        /// </summary>
        public WorkflowStateObject StateObject { get; internal set; }

        /// <summary>
        /// List of current wait nodes (infrastructure DTOs) representing what the workflow is waiting for.
        /// </summary>
        public List<WaitInfrastructureDto> Waits { get; internal set; } = new();

        /// <summary>
        /// Current status of the workflow instance.
        /// </summary>
        public WorkflowInstanceStatus Status { get; internal set; }

        /// <summary>
        /// History of cancellation events for this workflow instance.
        /// Used to determine which waits should be cancelled during evaluation.
        /// </summary>
        public List<CancellationHistoryEntry> CancellationHistory { get; internal set; } = new();

        public string WorkflowType { get; internal set; }
    }
}

