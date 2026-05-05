using System;
using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Abstraction.DTOs
{
    public class WorkflowExecutionRequest
    {
        public SignalDto Signal { get; set; }

        /// <summary>
        /// Command, Signal or TimeWait
        /// </summary>
        public Guid TriggeringWaitId { get; set; }
        public WorkflowStateDto WorkflowState { get; set; }
    }
}
