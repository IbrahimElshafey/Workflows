using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    /// <summary>
    /// Relational storage for a single child wait inside a massive fan-out (WaitMany/WaitAny).
    /// Keeping child metadata in a dedicated table avoids bloating the JSON state blob.
    /// </summary>
    public class ExternalChildWaitEntity : IEntity<string>
    {
        public string Id { get; set; } = string.Empty;
        public Guid WorkflowInstanceId { get; set; }

        /// <summary>
        /// Id of the parent ExternalGroupWaitDto.
        /// </summary>
        public string ParentWaitId { get; set; } = string.Empty;

        /// <summary>
        /// WaitType of the child (SignalWait, TimeWait, Command, etc.).
        /// </summary>
        public int ChildWaitType { get; set; }

        public int Status { get; set; }

        // Signal wait fields
        public string? SignalPath { get; set; }
        public string? SignalExactMatchPaths { get; set; }
        public string? ExactMatchFilter { get; set; }
        public bool IsFirstWait { get; set; }
        public string? TemplateHashKey { get; set; }

        // Time wait fields
        public string? UniqueMatchId { get; set; }
        public DateTime? ExecutionTime { get; set; }

        // Command wait fields
        public string? CommandWaitId { get; set; }

        // Compensation wait fields
        public string? Token { get; set; }

        public string? CancelTokens { get; set; }

        public DateTime Created { get; set; }
    }
}
