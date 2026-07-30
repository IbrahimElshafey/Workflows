using System;

namespace Workflows.Definition
{
    /// <summary>
    /// Exception thrown to halt migration when a workflow instance's state is unmigratable or deprecated.
    /// </summary>
    public class WorkflowMigrationCancelledException : Exception
    {
        public string Reason { get; }

        public WorkflowMigrationCancelledException(string reason)
            : base($"Workflow migration cancelled: {reason}")
        {
            Reason = reason;
        }
    }
}
