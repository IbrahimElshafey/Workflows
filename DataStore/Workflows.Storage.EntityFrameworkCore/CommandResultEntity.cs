using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class CommandResultEntity
    {
        public int Id { get; set; }
        public Guid GlobalId { get; set; }
        public DateTime ReceivedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public int Status { get; set; } // 0=Pending, 1=Processed, 2=Failed
        public string CommandWaitId { get; set; } = string.Empty;
        public string ResultJson { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
    }
}
