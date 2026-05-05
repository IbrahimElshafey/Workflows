using System.Collections.Generic;

namespace Workflows.Abstraction.DTOs.Registration
{
    public class BulkRegistrationPackage
    {
        public string RunnerName { get; set; }

        public List<WorkflowDefinition> Workflows { get; set; } = new();
        public List<SignalDefinition> Signals { get; set; } = new();
        public List<CommandDefinition> Commands { get; set; } = new();
    }
}