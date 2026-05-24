using System;

namespace Workflows.Abstraction.DTOs
{
    public class SignalDto
    {
        public Guid Id { get; set; }
        public object Data { get; set; }
        public DateTime ClientSentTime { get; set; }
        public DateTime OrchestratorReceiveTime { get; set; }
        public string SignalIdentifier { get; set; }
    }
}