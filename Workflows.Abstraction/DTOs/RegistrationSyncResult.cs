using System;
using System.Collections.Generic;

namespace Workflows.Abstraction.DTOs
{
    public class RegistrationSyncResult
    {
        public bool Success { get; set; }

        // Total counts for verification
        public int WorkflowsRegistered { get; set; }
        public int SignalsRegistered { get; set; }
        public int CommandsRegistered { get; set; }

        // Detailed errors (e.g., "Workflow 'Order' v1.0 already exists with different schema")
        public List<RegistrationError> Errors { get; set; } = new();

        public DateTime SyncTimestamp { get; set; }
    }
}
