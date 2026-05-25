using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class TimeWaitEntity : WorkflowWaitEntity
    {
        /// <summary>
        /// Absolute UTC time at which the timer fires.
        /// Computed at mapping time as DateTime.UtcNow + TimeWait.TimeToWait.
        /// </summary>
        public DateTime ExecutionTime { get; set; }

        /// <summary>
        /// Unique match identifier used as the signal path when the timer fires.
        /// </summary>
        public string UniqueMatchId { get; set; } = string.Empty;
    }
}
