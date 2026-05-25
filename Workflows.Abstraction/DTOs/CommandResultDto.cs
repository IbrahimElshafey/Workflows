using System;

namespace Workflows.Abstraction.DTOs
{
    /// <summary>
    /// Data Transfer Object representing the result of a command execution returned from external systems.
    /// </summary>
    public class CommandResultDto
    {
        public Guid CommandWaitId { get; set; }
        public object Result { get; set; }
        public DateTime ClientSentTime { get; set; }
        public DateTime OrchestratorReceiveTime { get; set; }
    }
}
