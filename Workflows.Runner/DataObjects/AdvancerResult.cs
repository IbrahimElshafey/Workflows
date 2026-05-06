using Workflows.Abstraction.DTOs;
using Workflows.Definition;

namespace Workflows.Runner.DataObjects
{
    public class AdvancerResult
    {
        public Wait Wait { get; set; }
        public StateMachineObject State { get; set; }
    }
}