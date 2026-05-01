namespace Workflows.Abstraction.DTOs
{
    public class WorkflowExecutionRequest
    {
        public SignalDto Signal { get; set; }
        public Definition.DTOs.WaitInfrastructureDto TriggeringWait { get; set; }
        public WorkflowStateDto WorkflowState { get; set; }
    }
}