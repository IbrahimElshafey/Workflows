using System;

namespace Workflows.Abstraction.DTOs.Waits
{
    /// <summary>
    /// DTO for SubWorkflowWait that represents a nested workflow execution.
    /// Inherits from WaitInfrastructureDto to maintain compatibility with persistence infrastructure.
    /// </summary>
    public sealed class SubWorkflowWaitDto : WaitInfrastructureDto
    {
        public int StateIndex { get; set; }
        public Guid StateMachineObjectId { get; set; }
        internal SubWorkflowWaitDto()
        {
        }
    }
}
