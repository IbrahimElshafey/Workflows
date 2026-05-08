using System;
using System.Collections.Generic;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;

namespace Workflows.Abstraction.DTOs
{
    /// <summary>
    /// Represents the persistent state of a workflow instance.
    /// Contains serialized workflow data and the current wait tree.
    /// </summary>
    public class WorkflowStateDto
    {
        /// <summary>
        /// Unique identifier for this workflow state.
        /// </summary>
        public Guid Id { get; internal set; }

        /// <summary>
        /// UTC timestamp when this workflow state was created.
        /// </summary>
        public DateTime Created { get; internal set; }

        /// <summary>
        /// Serialized instance that contains the resumable workflow instance data and all locals (closures,methods private data) needed for execustion.
        /// </summary>
        public StateMachineObject StateObject { get; internal set; }

        /// <summary>
        /// List of current wait nodes (infrastructure DTOs) representing what the workflow is waiting for.
        /// </summary>
        public List<WaitInfrastructureDto> Waits { get; internal set; } = new();

        /// <summary>
        /// Current status of the workflow instance.
        /// </summary>
        public WorkflowInstanceStatus Status { get; internal set; }

        /// <summary>
        /// Set of token IDs that have been explicitly cancelled during this workflow instance's lifetime.
        /// Passive waits referencing any of these tokens will be interrupted before evaluation.
        /// </summary>
        public HashSet<string> CancelledTokens { get; internal set; } = new HashSet<string>();
        public string WorkflowType { get; internal set; }
    }
}

