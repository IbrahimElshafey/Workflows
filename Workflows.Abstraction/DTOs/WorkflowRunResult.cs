
using System;
using System.Collections.Generic;
using System.Linq;
using Workflows.Abstraction.Enums;

namespace Workflows.Abstraction.DTOs
{
    public class WorkflowRunResult
    {
        public object WorkflowState { get; internal set; }

        public WaitBaseDto IncomingWait { get; internal set; }

        public WorkflowInstanceStatus Status { get; internal set; }
        public string Message { get; internal set; }
    }
}