using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Serializers
{
    /// <summary>
    /// Handles GroupWaitDto objects.
    /// Child waits are already DTOs due to the Wait -> DTO conversion.
    /// Returns false to suspend execution.
    /// </summary>
    internal class GroupWaitSerializer : WaitSerializer
    {
        public GroupWaitSerializer(Mapper mapper, StateMachineAdvancer stateMachineAdvancer)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            if (stateMachineAdvancer == null) throw new ArgumentNullException(nameof(stateMachineAdvancer));
        }

        public SerializerFactory ProcessorFactory { get; set; }

        public override Task<bool> Serialize(WaitInfrastructureDto yieldedWait, WorkflowExecutionContext context)
        {
            if (yieldedWait is not GroupWaitDto groupWaitDto)
            {
                throw new InvalidOperationException("GroupWaitSerializer requires a GroupWaitDto.");
            }

            // Save ExplicitState to WorkflowStateObject.Locals
            SaveWaitStatesToMachineState(groupWaitDto, context.WorkflowState.StateObject, context.WorkflowInstance);

            // Recursively save state for all child DTOs
            SaveChildStatesRecursive(groupWaitDto.ChildWaits, context);

            // Add parent group to waits collection
            context.WorkflowState.Waits.Add(groupWaitDto);

            // Return false - passive wait, suspend execution
            return Task.FromResult(false);
        }

        private void SaveChildStatesRecursive(IEnumerable<WaitInfrastructureDto> childWaits, WorkflowExecutionContext context)
        {
            if (childWaits == null) return;

            foreach (var child in childWaits)
            {
                if (child == null) continue;

                SaveWaitStatesToMachineState(child, context.WorkflowState.StateObject, context.WorkflowInstance);

                if (child.ChildWaits != null && child.ChildWaits.Any())
                {
                    SaveChildStatesRecursive(child.ChildWaits, context);
                }
            }
        }
    }
}
