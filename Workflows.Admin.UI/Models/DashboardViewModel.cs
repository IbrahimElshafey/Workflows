namespace Workflows.Admin.UI.Models
{
    /// <summary>
    /// View model for the admin dashboard overview page.
    /// </summary>
    public class DashboardViewModel
    {
        public int TotalInstances { get; set; }
        public int RunningInstances { get; set; }
        public int SuspendedInstances { get; set; }
        public int CompletedInstances { get; set; }
        public int FaultedInstances { get; set; }
        public int CanceledInstances { get; set; }

        public int ActiveSignalWaits { get; set; }
        public int ActiveCommandWaits { get; set; }
        public int ActiveTimeWaits { get; set; }
        public int ActiveCompensationWaits { get; set; }

        public double FailureRate { get; set; }
        public double AverageExecutionLatencyMs { get; set; }

        public List<WorkflowTypeCount> InstancesByWorkflowType { get; set; } = new();
        public List<DailyInstanceCount> DailyInstanceCounts { get; set; } = new();
        public List<AlertViewModel> Alerts { get; set; } = new();
    }

    public class WorkflowTypeCount
    {
        public string WorkflowType { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class DailyInstanceCount
    {
        public DateTime Date { get; set; }
        public int Started { get; set; }
        public int Completed { get; set; }
        public int Faulted { get; set; }
    }

    public class AlertViewModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public AlertSeverity Severity { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? InstanceId { get; set; }
        public DateTime DetectedAt { get; set; }
    }

    public enum AlertSeverity
    {
        Info,
        Warning,
        Critical
    }
}
