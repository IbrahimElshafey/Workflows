using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Processors
{
    /// <summary>
    /// Handles GroupWait objects.
    /// Unfolds composite layers and asserts that ChildWaitsRuntime contains only IPassiveWait references,
    /// throwing structural errors if active intents are nested inside a parallel pool.
    /// Returns false to suspend execution.
    /// </summary>
    internal class GroupWaitProcessor : WorkflowWaitProcessor
    {
        private readonly Mapper _mapper;

        public GroupWaitProcessor(Mapper mapper)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public override Task<bool> ProcessAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var groupWait = yieldedWait as GroupWait;
            if (groupWait == null)
            {
                throw new InvalidOperationException("GroupWaitProcessor requires a GroupWait.");
            }

            // Validate that all children are passive waits
            ValidateChildWaitsArePassive(groupWait);

            // Save ExplicitState to WorkflowStateObject.WaitStatesObjects
            SaveWaitStatesToMachineState(yieldedWait, context.ActiveState);

            // Map parent group to DTO
            var groupWaitDto = _mapper.MapToDto(groupWait) as GroupWaitDto;
            if (groupWaitDto == null)
            {
                throw new InvalidOperationException("Failed to map GroupWait to GroupWaitDto.");
            }

            // Recursively process and map all child waits
            groupWaitDto.ChildWaits = ProcessChildWaits(groupWait.ChildWaits, groupWait.Id, context);

            // Add parent group to new waits
            context.NewWaits.Add(groupWaitDto);

            // Return false - passive wait, suspend execution
            return Task.FromResult(false);
        }

        private void ValidateChildWaitsArePassive(GroupWait groupWait)
        {
            if (groupWait.ChildWaits == null || !groupWait.ChildWaits.Any())
            {
                return;
            }

            foreach (var child in groupWait.ChildWaits)
            {
                if (!(child is IPassiveWait))
                {
                    throw new InvalidOperationException(
                        $"GroupWait '{groupWait.WaitName}' contains non-passive wait '{child.WaitName}' of type {child.GetType().Name}. " +
                        "Only passive waits (SignalWait, TimeWait, DeferredCommand, SubWorkflowWait, nested GroupWait) are allowed in GroupWait.");
                }

                // Recursively validate nested groups
                if (child is GroupWait nestedGroup)
                {
                    ValidateChildWaitsArePassive(nestedGroup);
                }
            }
        }

        private List<WaitInfrastructureDto> ProcessChildWaits(
            IReadOnlyList<Wait> childWaits, 
            Guid parentWaitId, 
            WorkflowExecutionContext context)
        {
            if (childWaits == null || !childWaits.Any())
            {
                return new List<WaitInfrastructureDto>();
            }

            var childDtos = new List<WaitInfrastructureDto>();

            foreach (var child in childWaits)
            {
                // Save child wait states
                SaveWaitStatesToMachineState(child, context.ActiveState);

                // Map child to DTO
                var childDto = _mapper.MapToDto(child);
                childDto.ParentWaitId = parentWaitId;

                // If child is also a GroupWait, recursively process its children
                if (child is GroupWait nestedGroup)
                {
                    var nestedGroupDto = childDto as GroupWaitDto;
                    if (nestedGroupDto != null)
                    {
                        nestedGroupDto.ChildWaits = ProcessChildWaits(nestedGroup.ChildWaits, nestedGroup.Id, context);
                    }
                }
                // If child is a SubWorkflowWait with children, handle them
                else if (child is SubWorkflowWait subWorkflow && subWorkflow.ChildWaits != null && subWorkflow.ChildWaits.Any())
                {
                    var subWorkflowDto = childDto as SubWorkflowWaitDto;
                    if (subWorkflowDto != null)
                    {
                        subWorkflowDto.ChildWaits = new List<WaitInfrastructureDto>();
                        foreach (var subChild in subWorkflow.ChildWaits)
                        {
                            SaveWaitStatesToMachineState(subChild, context.ActiveState);
                            var subChildDto = _mapper.MapToDto(subChild);
                            subChildDto.ParentWaitId = subWorkflow.Id;
                            subWorkflowDto.ChildWaits.Add(subChildDto);
                        }
                    }
                }

                childDtos.Add(childDto);
            }

            return childDtos;
        }
    }
}

