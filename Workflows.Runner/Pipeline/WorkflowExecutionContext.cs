using System;
using System.Collections.Generic;
using Workflows.Abstraction.DTOs;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline
{
    /// <summary>
    /// Shared execution context passed through the matcher and processor pipelines.
    /// Contains all state required to match incoming events and process outgoing waits.
    /// Most state is stored in WorkflowState to avoid duplication.
    /// </summary>
    internal class WorkflowExecutionContext
    {
        /// <summary>
        /// The incoming trigger: either Signal or CommandResult (mutually exclusive).
        /// </summary>
        public SignalDto Signal { get; set; }
        public object CommandResult { get; set; }

        /// <summary>
        /// The workflow state containing all waits, status, and state objects.
        /// Use WorkflowState.Waits instead of NewWaits.
        /// Use WorkflowState.StateObject instead of ActiveState.
        /// Use WorkflowState.Status instead of IsWorkflowCompleted.
        /// </summary>
        public WorkflowStateDto WorkflowState { get; set; }

        public WorkflowContainer WorkflowInstance { get; set; }
        public Guid TriggeringWaitId { get; set; }

        /// <summary>
        /// The stream to advance (parent or child workflow).
        /// </summary>
        public IAsyncEnumerable<Wait> WorkflowStream { get; set; }

        /// <summary>
        /// Indicates whether the execution loop should continue immediately after processing a wait.
        /// Set to true for active waits (Immediate, Compensation), false for passive waits.
        /// </summary>
        public bool ContinueExecutionLoop { get; set; }

        /// <summary>
        /// Tracks IDs of waits that have been consumed/completed.
        /// </summary>
        public List<Guid> ConsumedWaitsIds { get; } = new List<Guid>();
    }
}
