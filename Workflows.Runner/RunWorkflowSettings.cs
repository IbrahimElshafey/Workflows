using Workflows.Abstraction.Enums;

namespace Workflows.Runner
{
    public class RunWorkflowSettings
    {
        public Definition.Enums.WaitStatus WaitStatusIfProcessingError { get; internal set; }
        internal bool UserSerialization { get;  set; }

    }
}
