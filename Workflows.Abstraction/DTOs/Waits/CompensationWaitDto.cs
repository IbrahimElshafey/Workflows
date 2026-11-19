using System;

namespace Workflows.Abstraction.DTOs.Waits
{
    /// <summary>
    /// DTO for CompensationWait representing a request to run compensation actions.
    /// </summary>
    public class CompensationWaitDto : WaitInfrastructureDto
    {
        public string Token { get; set; }
    }
}
