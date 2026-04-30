using System;
using System.Collections.Generic;
using Workflows.Abstraction.Enums;

namespace Workflows.Abstraction.DTOs
{
    /// <summary>
    /// Represents the persistent state of a workflow instance.
    /// Contains serialized workflow data and the current wait nodes.
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
        /// Serialized class instance that contains the resumable workflow instance data.
        /// </summary>
        public object StateObject { get; internal set; }

        /// <summary>
        /// List of current wait nodes (infrastructure DTOs) representing what the workflow is waiting for.
        /// </summary>
        public List<WaitInfrastructureDto> Waits { get; internal set; } = new();

        /// <summary>
        /// Current status of the workflow instance.
        /// </summary>
        public WorkflowInstanceStatus Status { get; internal set; }
    }
}
