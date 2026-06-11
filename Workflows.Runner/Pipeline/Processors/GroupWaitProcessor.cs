using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Processors
{
    /// <summary>
    /// Handles GroupWait objects.
    /// Unfolds composite layers and supports executing nested sub-workflows.
    /// Returns false to suspend execution.
    /// </summary>
    internal class GroupWaitProcessor : WorkflowWaitProcessor
    {
        private readonly Mapper _mapper;
        private readonly StateMachineAdvancer _stateMachineAdvancer;

        public ProcessorFactory ProcessorFactory { get; set; }

        public GroupWaitProcessor(Mapper mapper, StateMachineAdvancer stateMachineAdvancer)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _stateMachineAdvancer = stateMachineAdvancer ?? throw new ArgumentNullException(nameof(stateMachineAdvancer));
        }

        public override async Task<bool> ProcessAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var groupWait = yieldedWait as GroupWait;
            if (groupWait == null)
            {
                throw new InvalidOperationException("GroupWaitProcessor requires a GroupWait.");
            }

            // Save ExplicitState to WorkflowStateObject.WaitStatesObjects
            SaveWaitStatesToMachineState(yieldedWait, context.WorkflowState.StateObject);

            // Map parent group to DTO
            var groupWaitDto = _mapper.MapToDto(groupWait) as GroupWaitDto;
            if (groupWaitDto == null)
            {
                throw new InvalidOperationException("Failed to map GroupWait to GroupWaitDto.");
            }

            // Recursively process and map all child waits
            groupWaitDto.ChildWaits = await ProcessChildWaits(groupWait.ChildWaits, groupWait.Id, context).ConfigureAwait(false);

            // Add parent group to waits collection
            context.WorkflowState.Waits.Add(groupWaitDto);

            // Return false - passive wait, suspend execution
            return false;
        }

        private async Task<List<WaitInfrastructureDto>> ProcessChildWaits(
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
                SaveWaitStatesToMachineState(child, context.WorkflowState.StateObject);

                // Map child to DTO
                var childDto = _mapper.MapToDto(child);
                childDto.ParentWaitId = parentWaitId;

                // If child is also a GroupWait, recursively process its children
                if (child is GroupWait nestedGroup)
                {
                    var nestedGroupDto = childDto as GroupWaitDto;
                    if (nestedGroupDto != null)
                    {
                        nestedGroupDto.ChildWaits = await ProcessChildWaits(nestedGroup.ChildWaits, nestedGroup.Id, context).ConfigureAwait(false);
                    }
                }
                // If child is a SubWorkflowWait, process/advance it
                else if (child is SubWorkflowWait subWorkflow)
                {
                    var subWorkflowDto = childDto as SubWorkflowWaitDto;
                    if (subWorkflowDto != null)
                    {
                        subWorkflowDto.ChildWaits = new List<WaitInfrastructureDto>();
                        
                        var childKey = subWorkflowDto.StateMachineObjectId.ToString();
                        var childState = new WorkflowStateObject
                        {
                            WorkflowType = context.WorkflowState.WorkflowType,
                            SubWorkflowMethod = subWorkflowDto.CallerName
                        };
                        
                        bool subWorkflowCompleted = false;
                        var subWorkflowStream = subWorkflow.Runner;

                        while (!subWorkflowCompleted)
                        {
                            var advancerResult = await _stateMachineAdvancer.RunAsync(subWorkflowStream, childState).ConfigureAwait(false);
                            
                            if (advancerResult == null)
                            {
                                subWorkflowCompleted = true;
                                break;
                            }

                            var childWait = advancerResult.Wait;
                            childState = advancerResult.State;

                            if (childWait == null)
                            {
                                subWorkflowCompleted = true;
                                break;
                            }

                            // Save child wait states
                            SaveWaitStatesToMachineState(childWait, childState);

                            if (IsActiveWait(childWait))
                            {
                                var childProcessor = ProcessorFactory.GetProcessor(childWait);
                                bool childContinues = await childProcessor.ProcessAsync(childWait, context).ConfigureAwait(false);
                                
                                if (!childContinues)
                                {
                                    context.WorkflowState.StateObject.Locals[childKey] = childState;
                                    break;
                                }
                            }
                            else
                            {
                                // Passive wait
                                var subChildDto = _mapper.MapToDto(childWait);
                                subChildDto.ParentWaitId = subWorkflow.Id;
                                subWorkflowDto.ChildWaits.Add(subChildDto);
                                
                                // Store child state
                                context.WorkflowState.StateObject.Locals[childKey] = childState;
                                break;
                            }
                        }

                        if (subWorkflowCompleted)
                        {
                            context.WorkflowState.StateObject.Locals.Remove(childKey);
                            subWorkflowDto.Status = Abstraction.Enums.WaitStatus.Completed;
                        }
                    }
                }
                // If child is a SubWorkflowWait DTO with children already mapped
                else if (child is SubWorkflowWait subWorkflowLegacy && subWorkflowLegacy.ChildWaits != null && subWorkflowLegacy.ChildWaits.Any())
                {
                    var subWorkflowDto = childDto as SubWorkflowWaitDto;
                    if (subWorkflowDto != null)
                    {
                        subWorkflowDto.ChildWaits = new List<WaitInfrastructureDto>();
                        foreach (var subChild in subWorkflowLegacy.ChildWaits)
                        {
                            SaveWaitStatesToMachineState(subChild, context.WorkflowState.StateObject);
                            var subChildDto = _mapper.MapToDto(subChild);
                            subChildDto.ParentWaitId = subWorkflowLegacy.Id;
                            subWorkflowDto.ChildWaits.Add(subChildDto);
                        }
                    }
                }

                childDtos.Add(childDto);
            }

            return childDtos;
        }

        private bool IsActiveWait(Wait wait)
        {
            if (wait is CompensationWait || wait is SubWorkflowWait)
            {
                return true;
            }

            if (wait.WaitType == Workflows.Primitives.WaitType.Command)
            {
                var commandWaitType = wait.GetType();
                var executionModeProperty = commandWaitType.GetProperty("ExecutionMode",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                if (executionModeProperty != null)
                {
                    var executionMode = executionModeProperty.GetValue(wait);
                    if (executionMode != null && executionMode.ToString() == "Immediate")
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
