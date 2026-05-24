using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class WorkflowWait : IEntity<Guid>, IEntityWithDelete
    {
        public Guid Id { get; set; }
        public Guid WorkflowInstanceId { get; set; }
        public int Status { get; set; } // Map from WaitStatus
        public int StateAfterWait { get; set; }
        public Guid StateKey { get; set; }
        public Guid? ParentWaitId { get; set; }
        public string WaitName { get; set; } = string.Empty;
        public int WaitType { get; set; } // Map from WaitType
        public string DtoJson { get; set; } = string.Empty;
        public string DtoType { get; set; } = string.Empty;
        public string CancelTokens { get; set; } = string.Empty;

        public DateTime Created { get; set; }
        public bool IsDeleted { get; set; }
    }

    public class SignalWait : WorkflowWait
    {
        public string SignalPath { get; set; } = string.Empty;
    }

    public class CommandWait : WorkflowWait
    {
        public Guid CommandWaitId { get; set; }
    }
}
