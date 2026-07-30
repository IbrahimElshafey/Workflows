using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class WorkerCapabilityEntity : IEntity<Guid>
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string DllVersion { get; set; } = string.Empty;
        public string WorkflowType { get; set; } = string.Empty;
        public int WorkflowVersion { get; set; }
        public DateTime Created { get; set; } = DateTime.UtcNow;
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }
}

