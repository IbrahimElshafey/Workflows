using System.Collections.Generic;

using System;
using Workflows.Abstraction.Enums;

namespace Workflows.Abstraction.DTOs
{
    /// <summary>
    /// Core wait configuration DTO containing only essential wait definition properties.
    /// Lightweight and focused on workflow definition, not persistence infrastructure.
    /// </summary>
    public abstract class WaitCoreDto
    {
        /// <summary>
        /// The name/identifier of this wait.
        /// </summary>
        public string WaitName { get; internal set; }

        /// <summary>
        /// The type of wait (Signal, Time, Command, etc.).
        /// </summary>
        public WaitType WaitType { get; internal set; }

        /// <summary>
        /// Name of the method that created this wait.
        /// </summary>
        public string CallerName { get; internal set; }

        /// <summary>
        /// Line number in the source code where this wait was created.
        /// </summary>
        public int InCodeLine { get; internal set; }

        /// <summary>
        /// UTC timestamp when this wait was created.
        /// </summary>
        public DateTime Created { get; internal set; }
    }

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
        public WaitStatus Status { get; internal set; } = WaitStatus.Waiting;

        /// <summary>
        /// Local variables in the method at the wait point. Represents RunnerState.
        /// </summary>
        public PrivateData Locals { get; internal set; }

        /// <summary>
        /// Closure data captured at the wait point for later deserialization.
        /// </summary>
        public PrivateData ClosureData { get; internal set; }

        /// <summary>
        /// Persistence status of this wait.
        /// </summary>
        public PersistStatus PersistStatus { get; internal set; }

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