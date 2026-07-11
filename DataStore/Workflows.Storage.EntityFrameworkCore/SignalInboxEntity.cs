using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class SignalInboxEntity : IEntity
    {
        public string MessageId { get; set; } = string.Empty;
        public Guid WorkflowInstanceId { get; set; }
        public DateTime ProcessedAt { get; set; }
        public string PayloadHash { get; set; } = string.Empty;
        public DateTime Created { get; set; }
    }
}
