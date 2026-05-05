using Workflows.Definition;
using Workflows.Shared.DataObject;

namespace Workflows.Runner.DataObjects
{
    public class AdvancerResult
    {
        public Wait Wait { get; set; }
        public StateMachineObject State { get; set; }
    }
}