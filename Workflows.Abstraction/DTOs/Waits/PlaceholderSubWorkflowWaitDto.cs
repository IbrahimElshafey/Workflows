using System;

namespace Workflows.Abstraction.DTOs.Waits
{
    public sealed class PlaceholderSubWorkflowWaitDto : WaitInfrastructureDto
    {
        public string MethodFullPath { get; set; } = string.Empty;

        public PlaceholderSubWorkflowWaitDto()
        {
            WaitType = Primitives.WaitType.PlaceholderSubWorkflow;
        }
    }
}
