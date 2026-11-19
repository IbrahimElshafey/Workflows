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
    /// Handles ExternalGroupWaitDto (WaitMany / WaitAny) objects.
    /// Child waits are already DTOs due to the Wait -> DTO conversion.
    /// Stores child waits in ExternalChildWaits so they are persisted in the dedicated
    /// relational table instead of the JSON state blob.
    /// </summary>
    internal class ExternalGroupWaitSerializer : WaitSerializer
    {
        public ExternalGroupWaitSerializer(Mapper mapper, StateMachineAdvancer stateMachineAdvancer)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            if (stateMachineAdvancer == null) throw new ArgumentNullException(nameof(stateMachineAdvancer));
        }

        public SerializerFactory ProcessorFactory { get; set; }

        public override Task<bool> Serialize(WaitInfrastructureDto yieldedWait, WorkflowExecutionContext context)
        {
            if (yieldedWait is not ExternalGroupWaitDto externalGroupDto)
            {
                throw new InvalidOperationException("ExternalGroupWaitSerializer requires an ExternalGroupWaitDto.");
            }

            SaveWaitStatesToMachineState(externalGroupDto, context.WorkflowState.StateObject, context.WorkflowInstance);

            // Recursively save state for all child DTOs
            SaveChildStatesRecursive(externalGroupDto.ExternalChildWaits, context);

            externalGroupDto.ChildCount = externalGroupDto.ExternalChildWaits?.Count ?? 0;
            externalGroupDto.RequiredCompletedCount = externalGroupDto.WaitType == Workflows.Primitives.WaitType.WaitMany
                ? externalGroupDto.ChildCount
                : 1;

            context.WorkflowState.Waits.Add(externalGroupDto);

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
