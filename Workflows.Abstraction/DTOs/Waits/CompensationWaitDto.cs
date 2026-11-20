using System;

namespace Workflows.Abstraction.DTOs.Waits
{
    /// <summary>
    /// DTO for CompensationWait representing a request to run compensation actions.
    /// </summary>
    public class CompensationWaitDto : WaitInfrastructureDto
    {
        public string Token { get; set; }

        /// <summary>
        /// Number of compensation attempts already performed for this wait.
        /// Persisted so retry/backoff state survives process restarts.
        /// </summary>
        public int Attempts { get; set; }

        /// <summary>
        /// The error message produced by the most recent failed compensation attempt.
        /// Cleared when the compensation succeeds.
        /// </summary>
        public string? LastError { get; set; }

        /// <summary>
        /// UTC timestamp before which the compensation worker must not retry this wait.
        /// Null means the wait is eligible for immediate processing.
        /// </summary>
        public DateTime? NextAttemptUtc { get; set; }
    }
}
