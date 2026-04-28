
using System;
using System.Linq;

namespace Workflows.Abstraction.DTOs
{
    public class WorkflowRunContext
    {
        public SignalDto Signal { get; set; }
        public string WorkflowIdentifier { get; set; }
        public WorkflowStateDto WorkflowState { get; set; }
    }
}