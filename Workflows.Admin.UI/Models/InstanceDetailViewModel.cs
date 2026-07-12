using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;

namespace Workflows.Admin.UI.Models
{
    /// <summary>
    /// View model for the workflow instance detail page.
    /// </summary>
    public class InstanceDetailViewModel
    {
        public Guid Id { get; set; }
        public string WorkflowType { get; set; } = string.Empty;
        public int WorkflowVersion { get; set; }
        public WorkflowInstanceStatus Status { get; set; }
        public DateTime Created { get; set; }
        public DateTime? Modified { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? LockedBy { get; set; }
        public DateTime? LockedAt { get; set; }
        public DateTime? LockExpiresAt { get; set; }
        public string? ErrorMessage { get; set; }
        public string? LastAdvanceReason { get; set; }

        /// <summary>
        /// Serialized JSON representation of the workflow container instance variables.
        /// </summary>
        public string VariablesJson { get; set; } = "{}";

        /// <summary>
        /// Serialized JSON representation of the local state variables.
        /// </summary>
        public string LocalsJson { get; set; } = "{}";

        public List<WaitTreeNodeViewModel> WaitTree { get; set; } = new();
        public List<CancellationHistoryEntry> CancellationHistory { get; set; } = new();

        public bool CanExecuteActions { get; set; }
        public List<SignalWaitOption> ActiveSignalWaits { get; set; } = new();
    }

    public class WaitTreeNodeViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string WaitName { get; set; } = string.Empty;
        public string WaitType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? CallerName { get; set; }
        public int? InCodeLine { get; set; }
        public DateTime Created { get; set; }
        public List<WaitTreeNodeViewModel> ChildWaits { get; set; } = new();
        public List<string> CancelTokens { get; set; } = new();

        // Signal-specific
        public string? SignalIdentifier { get; set; }
        public string? ExactMatchFilter { get; set; }
        public bool IsFirstWait { get; set; }

        // Time-specific
        public DateTime? ExecutionTime { get; set; }
        public string? UniqueMatchId { get; set; }

        // Command-specific
        public string? HandlerKey { get; set; }
        public string? CommandWaitId { get; set; }

        // Compensation-specific
        public string? CompensationToken { get; set; }
    }

    public class SignalWaitOption
    {
        public string WaitId { get; set; } = string.Empty;
        public string SignalIdentifier { get; set; } = string.Empty;
        public string? ExactMatchFilter { get; set; }
    }
}
