using Workflows.Abstraction.Enums;

namespace Workflows.Admin.UI.Models
{
    /// <summary>
    /// View model for the workflow instance list page.
    /// </summary>
    public class InstanceListViewModel
    {
        public PagedResult<InstanceSummaryViewModel> PagedResult { get; set; } = new();
        public InstanceFilterViewModel Filter { get; set; } = new();
    }

    public class InstanceSummaryViewModel
    {
        public Guid Id { get; set; }
        public string WorkflowType { get; set; } = string.Empty;
        public int WorkflowVersion { get; set; }
        public WorkflowInstanceStatus Status { get; set; }
        public DateTime Created { get; set; }
        public DateTime? Modified { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? LockedBy { get; set; }
        public DateTime? LockExpiresAt { get; set; }
        public int ActiveWaitsCount { get; set; }
        public string? ErrorMessage { get; set; }
        public string? LastAdvanceReason { get; set; }
    }

    public class InstanceFilterViewModel
    {
        public string? InstanceId { get; set; }
        public string? WorkflowType { get; set; }
        public WorkflowInstanceStatus? Status { get; set; }
        public DateTime? CreatedAfter { get; set; }
        public DateTime? CreatedBefore { get; set; }
        public bool? HasError { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }
}
