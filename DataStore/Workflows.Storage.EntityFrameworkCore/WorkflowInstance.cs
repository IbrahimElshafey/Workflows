using System;
using System.Collections.Generic;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class WorkflowInstance : IEntity<Guid>, IEntityWithUpdate
    {
        public Guid Id { get; set; }
        public DateTime Created { get; set; }
        public DateTime? Modified { get; set; }
        public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();

        public int Status { get; set; } // Map from WorkflowInstanceStatus
        public string WorkflowType { get; set; } = string.Empty;
        public int WorkflowVersion { get; set; } = 1;

        // Distributed instance-level lock fields.
        // LockedBy holds the node identifier that currently owns the lock.
        // LockExpiresAt is the absolute UTC time at which the lock expires (TTL safety).
        public string? LockedBy { get; set; }
        public DateTime? LockedAt { get; set; }
        public DateTime? LockExpiresAt { get; set; }

        // JSON Owned property
        public WorkflowStateObject StateObject { get; set; } = new();

        // Stored using value converter to serialize/deserialize List<CancellationHistoryEntry> to JSON string
        public List<CancellationHistoryEntry> CancellationHistory { get; set; } = new();

        // Stored using value converter to serialize/deserialize List<WaitInfrastructureDto> to JSON string
        public List<WaitInfrastructureDto> Waits { get; set; } = new();
    }
}
