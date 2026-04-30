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

    /// <summary>
    /// Result of a workflow run operation containing execution status and state information.
    /// </summary>
    public class WorkflowRunResult
    {
        /// <summary>
        /// The workflow state object (serialized or instance).
        /// </summary>
        public object WorkflowState { get; internal set; }

        /// <summary>
        /// The incoming wait that triggered the execution (or the next wait if newly created).
        /// Uses WaitInfrastructureDto to represent the wait state.
        /// </summary>
        public WaitInfrastructureDto IncomingWait { get; internal set; }

        /// <summary>
        /// Current status of the workflow instance.
        /// </summary>
        public WorkflowInstanceStatus Status { get; internal set; }

        /// <summary>
        /// Status message describing the workflow execution result.
        /// </summary>
        public string Message { get; internal set; }
    }
}