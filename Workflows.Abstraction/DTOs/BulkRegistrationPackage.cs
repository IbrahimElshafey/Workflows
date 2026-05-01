using System;
using System.Collections.Generic;

namespace Workflows.Abstraction.DTOs
{
    public class BulkRegistrationPackage
    {
        public string RunnerId { get; set; }
        public string[] ListeningQueues { get; set; }

        public List<WorkflowDefinition> Workflows { get; set; } = new();
        public List<SignalDefinition> Signals { get; set; } = new();
        public List<CommandDefinition> Commands { get; set; } = new();
    }
}