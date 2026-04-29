namespace Workflows.Abstraction.DTOs
{
    public class WorkflowRunContext
    {
        public SignalDto Signal { get; set; }
        public string WorkflowTypeName { get; set; }
        public WorkflowStateDto WorkflowState { get; set; }
    }
}