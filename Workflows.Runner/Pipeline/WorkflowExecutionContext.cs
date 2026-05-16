using System;
using System.Collections.Generic;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline
{
    /// <summary>
    /// Shared execution context passed through the evaluation and handler pipelines.
    /// Contains all state required to evaluate incoming events and handle outgoing waits.
    /// </summary>
    public class WorkflowExecutionContext
    {
        public WorkflowExecutionRequest IncomingRequest { get; set; }
        public WorkflowStateDto WorkflowState { get; set; }
        public WorkflowContainer WorkflowInstance { get; set; }
        public Guid TriggeringWaitId { get; set; }
        public WaitInfrastructureDto TriggeringWaitDto { get; set; }
        public Wait TriggeringWait { get; set; }

        /// <summary>
        /// For sub-workflow scenarios, tracks the parent SubWorkflowWait.
        /// </summary>
        public SubWorkflowWait ParentSubWorkflow { get; set; }

        /// <summary>
        /// The active state machine state (parent or child).
        /// </summary>
        public WorkflowStateObject ActiveState { get; set; }

        /// <summary>
        /// The stream to advance (parent or child workflow).
        /// </summary>
        public IAsyncEnumerable<Wait> WorkflowStream { get; set; }

        /// <summary>
        /// Indicates whether the execution loop should continue immediately after handling a wait.
        /// Set to true for active waits (ImmediateCommand, Compensation), false for passive waits.
        /// </summary>
        public bool ContinueExecutionLoop { get; set; }

        /// <summary>
        /// Accumulates new waits that will be persisted.
        /// </summary>
        public List<WaitInfrastructureDto> NewWaits { get; } = new List<WaitInfrastructureDto>();

        /// <summary>
        /// Tracks IDs of waits that have been consumed/completed.
        /// </summary>
        public List<Guid> ConsumedWaitsIds { get; } = new List<Guid>();

        /// <summary>
        /// Set to true when the workflow completes.
        /// </summary>
        public bool IsWorkflowCompleted { get; set; }
    }
}
