using System;

namespace Workflows.Abstraction.DTOs
{
    /// <summary>
    /// DTO for Command that stores command execution configuration.
    /// Inherits from WaitInfrastructureDto to maintain compatibility with the wait persistence system.
    /// </summary>
    public class CommandWaitDto : WaitInfrastructureDto
    {
        /// <summary>
        /// The serialized command payload.
        /// Will be populated by the workflow runner when it has access to IObjectSerializer.
        /// </summary>
        public string SerializedCommand { get; set; }

        /// <summary>
        /// Maximum number of retry attempts for command execution.
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 1;

        /// <summary>
        /// Backoff time between retry attempts.
        /// </summary>
        public TimeSpan? RetryBackoff { get; set; }

        /// <summary>
        /// The name of the registered compensation method to run if the workflow rolls back.
        /// </summary>
        public string CompensationMethodName { get; set; }

        /// <summary>
        /// Serialized representation of the cancel action callback.
        /// </summary>
        public string CancelAction { get; set; }

        /// <summary>
        /// Serialized representation of the result action callback.
        /// </summary>
        public string ResultAction { get; set; }
    }
}
