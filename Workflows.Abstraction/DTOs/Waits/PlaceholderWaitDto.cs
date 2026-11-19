using System;

namespace Workflows.Abstraction.DTOs.Waits
{
    public sealed class PlaceholderWaitDto : WaitInfrastructureDto
    {
        public PlaceholderWaitDto()
        {
            WaitType = Primitives.WaitType.Placeholder;
        }
    }
}
