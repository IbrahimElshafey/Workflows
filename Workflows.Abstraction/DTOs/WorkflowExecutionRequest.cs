using Workflows.Definition.Data.DTOs;

namespace Workflows.Abstraction.DTOs
{
    public class WorkflowExecutionRequest
    {
        public SignalDto Signal { get; set; }
        public WaitInfrastructureDto TriggeringWait { get; set; }
        public WorkflowStateDto WorkflowState { get; set; }
    }
}