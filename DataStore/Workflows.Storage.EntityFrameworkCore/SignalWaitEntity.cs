using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class SignalWaitEntity : WorkflowWaitEntity
    {
        public string SignalPath { get; set; } = string.Empty;
        public string SignalExactMatchPaths { get; set; } = string.Empty;
        public string ExactMatchFilter { get; set; } = string.Empty;
        public bool IsFirstWait { get; set; }
    }
}
