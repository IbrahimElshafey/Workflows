
using System;
using Workflows.Abstraction.Enums;

namespace Workflows.Abstraction.DTOs
{
    public class WorkflowRunId
    {
        public Guid Id { get; internal set; }
        public string Name { get; internal set; }
        public string Description { get; internal set; }
    }
    public class WorkflowRunResult
    {
        public object WorkflowState { get; internal set; }

        public WaitBaseDto IncomingWait { get; internal set; }

        public WorkflowInstanceStatus Status { get; internal set; }
        public string Message { get; internal set; }
    }
}