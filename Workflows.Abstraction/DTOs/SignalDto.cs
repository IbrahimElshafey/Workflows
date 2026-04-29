using System;

namespace Workflows.Abstraction.DTOs
{
    public class SignalDto
    {
        public Guid Id { get; internal set; }
        public object Data { get; internal set; }
        public DateTime Created { get; internal set; }
        public string SignalIdentifier { get; internal set; }
    }
}