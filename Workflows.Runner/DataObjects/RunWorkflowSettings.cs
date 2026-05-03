using Workflows.Abstraction.Enums;

namespace Workflows.Runner.DataObjects
{
    public class RunWorkflowSettings
    {
        public WaitStatus WaitStatusIfProcessingError { get; internal set; }
        internal bool UserSerialization { get;  set; }

    }
}


