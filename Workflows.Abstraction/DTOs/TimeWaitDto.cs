using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Workflows.Handler.Helpers;

namespace Workflows.Abstraction.DTOs
{
    public class TimeWaitDto : WaitBaseDto
    {
        public TimeSpan TimeToWait { get; set; }
        public string UniqueMatchId { get; set; }
        public string CancelAction { get; set; }
    }
}