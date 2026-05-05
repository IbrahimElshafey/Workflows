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
        /// The duration or until time to wait.
        /// </summary>
        public TimeSpan TimeToWait { get; set; }

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
