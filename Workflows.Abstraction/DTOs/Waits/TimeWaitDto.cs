using System;

namespace Workflows.Abstraction.DTOs.Waits
{
    /// <summary>
    /// DTO for TimeWait that stores time-based wait configuration.
    /// Inherits from WaitInfrastructureDto to maintain compatibility with persistence infrastructure.
    /// </summary>
    public class TimeWaitDto : WaitInfrastructureDto
    {
        /// <summary>
        /// Absolute UTC date/time at which the timer fires.
        /// Computed once at mapping time as DateTime.UtcNow + TimeWait.TimeToWait.
        /// </summary>
        public DateTime ExecutionTime { get; set; }

        /// <summary>
        /// Unique match identifier for time-based matching.
        /// </summary>
        public string UniqueMatchId { get; set; }

        /// <summary>
        /// Serialized callback to execute if this wait is cancelled.
        /// </summary>
        public string CancelAction { get; set; }
    }
}
