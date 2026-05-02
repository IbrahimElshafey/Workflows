
using System;

using System.Collections.Generic;

namespace Workflows.Definition.Data.DTOs
{
    /// <summary>
    /// Infrastructure and execution state DTO containing properties needed for persistence,
    /// runtime state tracking, and workflow execution coordination.
    /// </summary>
    public abstract class WaitInfrastructureDto : WaitCoreDto
    {
        /// <summary>
        /// Unique identifier for this wait instance.
        /// </summary>
        public Guid Id { get; internal set; } = new Guid();

        /// <summary>
        /// Current execution status of this wait (Waiting, Completed, Cancelled, etc.).
        /// </summary>
        public Enums.WaitStatus Status { get; internal set; } = Enums.WaitStatus.Waiting;

        /// <summary>
        /// Local variables in the method at the wait point. Represents RunnerState.
        /// </summary>
        public PrivateData Locals { get; internal set; }

        /// <summary>
        /// Closure data captured at the wait point for later deserialization.
        /// </summary>
        public PrivateData ClosureData { get; internal set; }

        /// <summary>
        /// State value after this wait completes (used for resumption logic).
        /// </summary>
        public int StateAfterWait { get; internal set; }

        /// <summary>
        /// Serialized path to this wait in the workflow structure.
        /// </summary>
        public string Path { get; internal set; }

        /// <summary>
        /// ID of the parent wait (if this wait is part of a group).
        /// </summary>
        public Guid? ParentWaitId { get; set; }

        /// <summary>
        /// ID of the workflow that requested this wait.
        /// </summary>
        public Guid RequestedByWorkflowId { get; set; }

        /// <summary>
        /// ID of the root workflow in the execution hierarchy.
        /// </summary>
        public Guid RootWorkflowId { get; internal set; }

        /// <summary>
        /// ID of the workflow state snapshot associated with this wait.
        /// </summary>
        public Guid WorkflowStateId { get; set; }

        /// <summary>
        /// Child waits if this is a composite wait (e.g., GroupWait).
        /// </summary>
        public List<WaitInfrastructureDto> ChildWaits { get; set; } = new();

        /// <summary>
        /// Token IDs that, when cancelled, will interrupt this passive wait before evaluation.
        /// </summary>
        public HashSet<string> CancelTokens { get; set; }
    }
}