using System;
using System.Threading.Tasks;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Processors
{
    /// <summary>
    /// Handles SubWorkflowWait objects.
    /// Extracts the target child IAsyncEnumerable stream, drives the sub-workflow's initial entry loop
    /// to extract its first inner yielded Wait primitive, and hands that initial child wait down to
    /// its matching specific handler. Returns child continuation outcome (true/false).
    /// </summary>
    internal class SubWorkflowProcessor : WorkflowWaitProcessor
    {
        private readonly StateMachineAdvancer _stateMachineAdvancer;
        private readonly ProcessorFactory _handlerFactory;
        private readonly Mapper _mapper;

        public SubWorkflowProcessor(
            StateMachineAdvancer stateMachineAdvancer,
            ProcessorFactory handlerFactory,
            Mapper mapper)
        {
            _stateMachineAdvancer = stateMachineAdvancer ?? throw new ArgumentNullException(nameof(stateMachineAdvancer));
            _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public override async Task<bool> ProcessAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var subWorkflowWait = yieldedWait as SubWorkflowWait;
            if (subWorkflowWait == null)
            {
                throw new InvalidOperationException("SubWorkflowProcessor requires a SubWorkflowWait.");
            }

            if (subWorkflowWait.Runner == null)
            {
                throw new InvalidOperationException($"Sub-workflow '{subWorkflowWait.WaitName}' has no Runner.");
            }

            var subWorkflowDto = _mapper.MapToDto(subWorkflowWait) as Workflows.Abstraction.DTOs.Waits.SubWorkflowWaitDto;
            if (subWorkflowDto == null)
            {
                throw new InvalidOperationException("Failed to map SubWorkflowWait to SubWorkflowWaitDto.");
            }

            var childKey = subWorkflowDto.StateMachineObjectId.ToString();

            // Create a new state object for the child workflow
            var childState = new Workflows.Abstraction.DTOs.WorkflowStateObject
            {
                WorkflowType = context.WorkflowState.WorkflowType,
                SubWorkflowMethod = subWorkflowDto.CallerName
            };
            
            bool subWorkflowCompleted = false;
            var subWorkflowStream = subWorkflowWait.Runner;

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
                    if (childWait is SubWorkflowWait nestedSubWorkflow)
                    {
                        var nestedProcessor = _handlerFactory.GetProcessor(childWait);
                        bool nestedContinues = await nestedProcessor.ProcessAsync(childWait, context).ConfigureAwait(false);
                        
                        if (!nestedContinues)
                        {
                            // Nested sub-workflow suspended
                            context.WorkflowState.StateObject.Locals[childKey] = childState;
                            SaveWaitStatesToMachineState(subWorkflowWait, context.WorkflowState.StateObject);
                            context.WorkflowState.Waits.Add(subWorkflowDto);
                            return false;
                        }
                    }
                    else
                    {
                        var childProcessor = _handlerFactory.GetProcessor(childWait);
                        bool childContinues = await childProcessor.ProcessAsync(childWait, context).ConfigureAwait(false);
                        
                        if (!childContinues)
                        {
                            // Active wait suspended
                            context.WorkflowState.StateObject.Locals[childKey] = childState;
                            SaveWaitStatesToMachineState(subWorkflowWait, context.WorkflowState.StateObject);
                            context.WorkflowState.Waits.Add(subWorkflowDto);
                            return false;
                        }
                    }
                }
                else
                {
                    // Passive wait - map to DTO and set parent reference
                    var childDto = _mapper.MapToDto(childWait);
                    childDto.ParentWaitId = subWorkflowWait.Id;

                    // Add child to parent's ChildWaits
                    subWorkflowDto.ChildWaits = new System.Collections.Generic.List<Workflows.Abstraction.DTOs.Waits.WaitInfrastructureDto> { childDto };

                    // Add parent sub-workflow to waits collection
                    context.WorkflowState.Waits.Add(subWorkflowDto);

                    // Store child state in the parent's state machine objects
                    context.WorkflowState.StateObject.Locals[childKey] = childState;

                    // Save parent sub-workflow wait states
                    SaveWaitStatesToMachineState(subWorkflowWait, context.WorkflowState.StateObject);

                    return false;
                }
            }

            // Sub-workflow completed natively
            context.WorkflowState.StateObject.Locals.Remove(childKey);
            return true;
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
