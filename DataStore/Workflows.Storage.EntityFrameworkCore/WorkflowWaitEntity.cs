using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class WorkflowWaitEntity : IEntity<Guid>
    {
        public Guid Id { get; set; }
        public Guid WorkflowInstanceId { get; set; }
        public int Status { get; set; } // Map from WaitStatus

        public DateTime Created { get; set; }
    }
}
