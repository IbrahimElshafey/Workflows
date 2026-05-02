using Workflows.Abstraction.Enums;
using Workflows.Definition.Data.Enums;

namespace Workflows.Runner
{
    public class RunWorkflowSettings
    {
        public WaitStatus WaitStatusIfProcessingError { get; internal set; }
        internal bool UserSerialization { get;  set; }

    }
}
