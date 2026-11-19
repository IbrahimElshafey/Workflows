using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class CommandWaitEntity : WorkflowWaitEntity
    {
        public string CommandWaitId { get; set; } = string.Empty;
    }
}
