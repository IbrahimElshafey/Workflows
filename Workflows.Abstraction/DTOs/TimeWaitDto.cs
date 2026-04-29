using System;

namespace Workflows.Abstraction.DTOs
{
    public class TimeWaitDto : WaitBaseDto
    {
        public TimeSpan TimeToWait { get; set; }
        public string UniqueMatchId { get; set; }
        public string CancelAction { get; set; }
    }
}