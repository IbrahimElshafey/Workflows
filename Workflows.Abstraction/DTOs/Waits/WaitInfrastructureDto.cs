using System;
using System.Collections.Generic;
using Workflows.Abstraction.Enums;

namespace Workflows.Abstraction.DTOs.Waits
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
        public string Id { get; internal set; } = string.Empty;

        /// <summary>
        /// Current execution status of this wait (Waiting, Completed, Cancelled, etc.).
        /// </summary>
        public WaitStatus Status { get; set; } = WaitStatus.Waiting;

        /// <summary>
        /// State value after this wait completes (used for resumption logic).
        /// </summary>
        public int StateAfterWait { get; internal set; }
        public Guid StateKey { get; set; }

        /// <summary>
        /// ID of the parent wait (if this wait is part of a group).
        /// </summary>
        public string? ParentWaitId { get; set; }

        /// <summary>
        /// Child waits if this is a composite wait (e.g., GroupWait).
        /// </summary>
        public List<WaitInfrastructureDto> ChildWaits { get; set; } = new();

        /// <summary>
        /// Token IDs that, when cancelled, will interrupt this passive wait before evaluation.
        /// </summary>
        public HashSet<string> CancelTokens { get; set; }

        /// <summary>
        /// Indication of whether this wait has been persisted to the DB query tables.
        /// </summary>
        public bool IsPersisted { get; set; }
    }
}
