using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class OutboxMessageEntity
    {
        public int Id { get; set; }
        public Guid GlobalId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public int Status { get; set; } // 0=Pending, 1=Sent, 2=Failed
        public string MessageType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public Guid WorkflowInstanceId { get; set; }
        public Guid CommandWaitId { get; set; }
    }
}
