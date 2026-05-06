using System;
using System.Collections.Generic;

namespace Workflows.Primitives
{
    public class RegistrationSyncResult
    {
        public bool Success { get; set; }
        public int WorkflowsRegistered { get; set; }
        public int SignalsRegistered { get; set; }
        public int CommandsRegistered { get; set; }
        public List<RegistrationError> Errors { get; set; } = new();
        public DateTime SyncTimestamp { get; set; }
    }
}
