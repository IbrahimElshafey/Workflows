namespace Workflows.Definition.DTOs
{
    /// <summary>
    /// DTO for SubWorkflowWait that represents a nested workflow execution.
    /// Inherits from WaitInfrastructureDto to maintain compatibility with persistence infrastructure.
    /// </summary>
    public sealed class SubWorkflowWaitDto : DTOs.WaitInfrastructureDto
    {
        internal SubWorkflowWaitDto()
        {
        }
    }
}
