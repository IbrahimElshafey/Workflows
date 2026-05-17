using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Workflows.Orchestrator.Data.EF.Entities
{
    public class WaitEntity
    {
        [Key]
        public Guid Id { get; set; }

        public Guid WorkflowInstanceId { get; set; }

        [MaxLength(200)]
        public string WaitType { get; set; } = null!;

        [MaxLength(200)]
        public string SignalIdentifier { get; set; } = null!;

        public string? ExactMatchPart { get; set; }

        public string? SignalExactMatchPaths { get; set; }

        public string WaitDtoJson { get; set; } = null!;

        [ForeignKey(nameof(WorkflowInstanceId))]
        public WorkflowStateEntity WorkflowState { get; set; } = null!;
    }
}
