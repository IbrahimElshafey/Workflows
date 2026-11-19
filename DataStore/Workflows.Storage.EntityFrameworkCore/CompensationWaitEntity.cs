using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class CompensationWaitEntity : WorkflowWaitEntity
    {
        public string Token { get; set; } = string.Empty;
    }
}
