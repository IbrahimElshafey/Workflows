using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Workflows.Orchestrator.Data.EF.Entities
{
    public class WorkflowStateEntity
    {
        [Key]
        public Guid Id { get; set; }

        [MaxLength(200)]
        public string WorkflowType { get; set; } = null!;

        public int Version { get; set; }

        public string StateJson { get; set; } = null!;

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public ICollection<WaitEntity> Waits { get; set; } = new List<WaitEntity>();
    }
}
