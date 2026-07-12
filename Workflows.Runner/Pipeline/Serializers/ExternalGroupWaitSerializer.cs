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
    /// Handles ExternalGroupWait (WaitMany / WaitAny) objects.
    /// Maps the parent to ExternalGroupWaitDto and stores child waits in ExternalChildWaits
    /// so they are persisted in the dedicated relational table instead of the JSON state blob.
    /// </summary>
    internal class ExternalGroupWaitSerializer : WaitSerializer
    {
        private readonly Mapper _mapper;
        private readonly StateMachineAdvancer _stateMachineAdvancer;

        public SerializerFactory ProcessorFactory { get; set; }

        public ExternalGroupWaitSerializer(Mapper mapper, StateMachineAdvancer stateMachineAdvancer)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _stateMachineAdvancer = stateMachineAdvancer ?? throw new ArgumentNullException(nameof(stateMachineAdvancer));
        }

        public override async Task<bool> Serialize(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var externalGroupWait = yieldedWait as ExternalGroupWait;
            if (externalGroupWait == null)
            {
                throw new InvalidOperationException("ExternalGroupWaitSerializer requires an ExternalGroupWait.");
            }

            SaveWaitStatesToMachineState(yieldedWait, context.WorkflowState.StateObject);

            var externalGroupDto = _mapper.MapToDto(externalGroupWait) as ExternalGroupWaitDto;
            if (externalGroupDto == null)
            {
                throw new InvalidOperationException("Failed to map ExternalGroupWait to ExternalGroupWaitDto.");
            }

            externalGroupDto.ExternalChildWaits = await ProcessChildWaits(externalGroupWait.ChildWaits, externalGroupWait.Id, context).ConfigureAwait(false);
            externalGroupDto.ChildCount = externalGroupDto.ExternalChildWaits.Count;
            externalGroupDto.RequiredCompletedCount = externalGroupWait.WaitType == Workflows.Primitives.WaitType.WaitMany
                ? externalGroupDto.ChildCount
                : 1;

            context.WorkflowState.Waits.Add(externalGroupDto);

            return false;
        }

        private async Task<List<WaitInfrastructureDto>> ProcessChildWaits(
            IReadOnlyList<Wait> childWaits,
            string parentWaitId,
            WorkflowExecutionContext context)
        {
            if (childWaits == null || !childWaits.Any())
            {
                return new List<WaitInfrastructureDto>();
            }

            var childDtos = new List<WaitInfrastructureDto>();

            foreach (var child in childWaits)
            {
                SaveWaitStatesToMachineState(child, context.WorkflowState.StateObject);

                var childDto = _mapper.MapToDto(child);
                childDto.ParentWaitId = parentWaitId;

                if (child is GroupWait nestedGroup)
                {
                    var nestedGroupDto = childDto as GroupWaitDto;
                    if (nestedGroupDto != null)
                    {
                        nestedGroupDto.ChildWaits = await ProcessNestedGroupChildren(nestedGroup.ChildWaits, nestedGroup.Id, context).ConfigureAwait(false);
                    }
                }
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

                            SaveWaitStatesToMachineState(childWait, childState);

                            if (IsActiveWait(childWait))
                            {
                                var childProcessor = ProcessorFactory.GetSerializer(childWait);
                                bool childContinues = await childProcessor.Serialize(childWait, context).ConfigureAwait(false);

                                if (!childContinues)
                                {
                                    context.WorkflowState.StateObject.Locals[childKey] = childState;
                                    break;
                                }
                            }
                            else
                            {
                                var subChildDto = _mapper.MapToDto(childWait);
                                subChildDto.ParentWaitId = subWorkflow.Id;
                                subWorkflowDto.ChildWaits.Add(subChildDto);

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

                childDtos.Add(childDto);
            }

            return childDtos;
        }

        private async Task<List<WaitInfrastructureDto>> ProcessNestedGroupChildren(
            IReadOnlyList<Wait> childWaits,
            string parentWaitId,
            WorkflowExecutionContext context)
        {
            if (childWaits == null || !childWaits.Any())
            {
                return new List<WaitInfrastructureDto>();
            }

            var childDtos = new List<WaitInfrastructureDto>();

            foreach (var child in childWaits)
            {
                SaveWaitStatesToMachineState(child, context.WorkflowState.StateObject);

                var childDto = _mapper.MapToDto(child);
                childDto.ParentWaitId = parentWaitId;

                if (child is GroupWait nestedGroup)
                {
                    var nestedGroupDto = childDto as GroupWaitDto;
                    if (nestedGroupDto != null)
                    {
                        nestedGroupDto.ChildWaits = await ProcessNestedGroupChildren(nestedGroup.ChildWaits, nestedGroup.Id, context).ConfigureAwait(false);
                    }
                }

                childDtos.Add(childDto);
            }

            return childDtos;
        }

        private bool IsActiveWait(Wait wait)
        {
            return false;
        }
    }
}
