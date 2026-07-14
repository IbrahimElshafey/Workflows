using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Serializers
{
    /// <summary>
    /// Base class for wait processors that serialize yielded wait DTOs after state machine advancement.
    /// </summary>
    internal abstract class WaitSerializer
    {
        /// <summary>
        /// Serializes the yielded wait DTO and returns whether the execution loop should be cached or suspended.
        /// </summary>
        public abstract Task<bool> Serialize(WaitInfrastructureDto yieldedWait, WorkflowExecutionContext context);

        /// <summary>
        /// Helper to save wait explicit state to machine state object.
        /// The state is looked up from the workflow container's WaitsStates dictionary by StateKey.
        /// </summary>
        protected void SaveWaitStatesToMachineState(WaitInfrastructureDto waitDto, WorkflowStateObject stateObject, WorkflowContainer workflowInstance)
        {
            if (waitDto == null || workflowInstance?.WaitsStates == null) return;
            if (waitDto.StateKey == Guid.Empty) return;

            stateObject.Locals ??= new Dictionary<string, object>();

            if (!workflowInstance.WaitsStates.TryGetValue(waitDto.StateKey, out var explicitState) || explicitState == null)
            {
                return;
            }

            if (!stateObject.Locals.ContainsKey(waitDto.StateKey.ToString()))
            {
                stateObject.Locals[waitDto.StateKey.ToString()] = explicitState;
            }

            if (!stateObject.Locals.ContainsKey(waitDto.Id))
            {
                stateObject.Locals[waitDto.Id] = explicitState;
            }
        }
    }
}
