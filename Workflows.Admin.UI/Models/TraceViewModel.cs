namespace Workflows.Admin.UI.Models
{
    /// <summary>
    /// View model for the execution trace viewer page.
    /// </summary>
    public class TraceViewModel
    {
        public Guid InstanceId { get; set; }
        public string WorkflowType { get; set; } = string.Empty;
        public List<TraceEntryViewModel> Entries { get; set; } = new();
    }

    public class TraceEntryViewModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = string.Empty; // SignalReceived, CommandDispatched, CommandCompleted, WaitCreated, WaitCompleted, Cancellation, Compensation
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? WaitId { get; set; }
        public string? SignalIdentifier { get; set; }
        public string? CommandWaitId { get; set; }
        public string? PayloadJson { get; set; }
        public string? ResultJson { get; set; }
        public bool IsCompensation { get; set; }
        public bool IsSuccess { get; set; }
    }
}
