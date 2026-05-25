using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class CommandWaitEntity : WorkflowWaitEntity
    {
        public Guid CommandWaitId { get; set; }
    }
}
