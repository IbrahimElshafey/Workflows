using System;

namespace Workflows.Definition
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class WorkflowMigrationAttribute : Attribute
    {
        public string WorkflowName { get; }
        public int    FromVersion  { get; }
        public int    ToVersion    { get; }

        public WorkflowMigrationAttribute(string workflowName, int fromVersion, int toVersion)
        {
            WorkflowName = workflowName;
            FromVersion  = fromVersion;
            ToVersion    = toVersion;
        }
    }
}
