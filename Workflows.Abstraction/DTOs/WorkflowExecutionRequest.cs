using System;
using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Abstraction.DTOs
{
    /// <summary>
    /// Represents an incoming event that triggers workflow execution.
    /// Contains either a signal payload or a command result, along with the workflow state.
    /// </summary>
    public class WorkflowExecutionRequest
    {
        /// <summary>
        /// The wait ID that is being triggered (Signal, Command, TimeWait, etc.)
        /// </summary>
        public Guid TriggeringWaitId { get; set; }

        /// <summary>
        /// The persisted workflow state to resume.
        /// </summary>
        public WorkflowStateDto WorkflowState { get; set; }

        /// <summary>
        /// Incoming signal payload (for SignalWait triggers).
        /// Mutually exclusive with CommandResult.
        /// </summary>
        public SignalDto Signal { get; set; }

        /// <summary>
        /// Result from external command execution (for deferred/dispatched CommandWait triggers).
        /// Mutually exclusive with Signal.
        /// </summary>
        public object CommandResult { get; set; }
    }
}
