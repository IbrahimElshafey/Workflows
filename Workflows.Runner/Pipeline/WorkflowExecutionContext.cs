using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;
using Workflows.Runner.Pipeline.Serializers;

namespace Workflows.Runner.Pipeline
{
    /// <summary>
    /// Shared execution context passed through the matcher and processor pipelines.
    /// Contains all state required to match incoming events and process outgoing waits.
    /// Most state is stored in WorkflowState to avoid duplication.
    /// </summary>
    internal class WorkflowExecutionContext
    {
        private readonly SerializerFactory _serializerFactory;

        public WorkflowExecutionContext(SerializerFactory serializerFactory)
        {
            _serializerFactory = serializerFactory ?? throw new ArgumentNullException(nameof(serializerFactory));
        }

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
        public string TriggeringWaitId { get; set; } = string.Empty;

        /// <summary>
        /// The stream to advance (parent or child workflow).
        /// </summary>
        public IAsyncEnumerable<WaitInfrastructureDto> WorkflowStream { get; set; }

        /// <summary>
        /// Indicates whether the execution loop should continue immediately after processing a wait.
        /// Set to true for active waits (Immediate, Compensation), false for passive waits.
        /// </summary>
        public bool ContinueExecutionLoop { get; set; }

        /// <summary>
        /// Indicates whether the workflow instance should be kept in the memory cache.
        /// </summary>
        public bool KeepInCache { get; set; }

        /// <summary>
        /// The currently active sub-workflow wait DTO being executed.
        /// </summary>
        public SubWorkflowWaitDto? CurrentSubWorkflow { get; set; }

        /// <summary>
        /// Tracks IDs of waits that have been consumed/completed.
        /// </summary>
        public List<string> ConsumedWaitsIds { get; } = new List<string>();

        /// <summary>
        /// Serializes the yielded wait DTO and updates the context state.
        /// </summary>
        public async Task SaveStateAsync(WaitInfrastructureDto yieldedWait)
        {
            if (yieldedWait == null) throw new ArgumentNullException(nameof(yieldedWait));

            var serializer = _serializerFactory.GetSerializer(yieldedWait);
            
            var originalWaitsCount = WorkflowState.Waits.Count;
            
            // Serialize wait and determine if it should be kept in the cache
            KeepInCache = await serializer.Serialize(yieldedWait, this);
            
            // If we are inside a sub-workflow, nest the serialized DTO under the sub-workflow DTO
            if (CurrentSubWorkflow != null && WorkflowState.Waits.Count > originalWaitsCount)
            {
                var newlyAddedDto = WorkflowState.Waits[WorkflowState.Waits.Count - 1];
                WorkflowState.Waits.RemoveAt(WorkflowState.Waits.Count - 1);
                
                newlyAddedDto.ParentWaitId = CurrentSubWorkflow.Id;
                CurrentSubWorkflow.ChildWaits.Add(newlyAddedDto);
            }
            
            // All yielded waits suspend execution
            ContinueExecutionLoop = false;
        }
    }
}
