using System;

namespace Workflows.Abstraction.DTOs
{
    public class SignalDto
    {
        public Guid Id { get; internal set; }
        public object Data { get; internal set; }
        public DateTime ClientSentTime { get; internal set; }
        public DateTime OrchestratorReceiveTime { get; internal set; }
        public string SignalIdentifier { get; internal set; }
    }
}