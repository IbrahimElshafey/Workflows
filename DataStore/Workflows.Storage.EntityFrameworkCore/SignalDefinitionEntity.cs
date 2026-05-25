using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class SignalDefinitionEntity : IEntity
    {
        public string SignalIdentifier { get; set; } = string.Empty;
        public string PayloadTypeName { get; set; } = string.Empty;
        public string PayloadSchema { get; set; } = string.Empty;
        public DateTime Created { get; set; }
    }
}
