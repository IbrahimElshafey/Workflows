using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class CompensationWaitEntity : WorkflowWaitEntity
    {
        public string Token { get; set; } = string.Empty;

        /// <summary>Number of compensation attempts already performed.</summary>
        public int Attempts { get; set; }

        /// <summary>Last compensation error message, when the most recent attempt failed.</summary>
        public string? LastError { get; set; }

        /// <summary>UTC timestamp before which no retry should be attempted.</summary>
        public DateTime? NextAttemptUtc { get; set; }    }
}
