using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Runner.DataObjects
{
    public class AdvancerResult
    {
        public WaitInfrastructureDto Wait { get; set; }
        public WorkflowStateObject State { get; set; }
    }
}
