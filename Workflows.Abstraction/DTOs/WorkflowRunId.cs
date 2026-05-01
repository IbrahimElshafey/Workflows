using System;

namespace Workflows.Abstraction.DTOs
{
    public class WorkflowRunId
    {
        public Guid Id { get; internal set; }
        public string Name { get; internal set; }
        public string Description { get; internal set; }
    }
}