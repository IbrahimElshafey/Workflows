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

            // Create a new state object for the child workflow
            var childState = new Workflows.Abstraction.DTOs.WorkflowStateObject();

            // Drive the sub-workflow's initial entry loop to get the first yielded wait
            var advancerResult = await _stateMachineAdvancer.RunAsync(subWorkflowWait.Runner, childState).ConfigureAwait(false);

            if (advancerResult?.Wait != null)
            {
                var childWait = advancerResult.Wait;

                // Store the child state in the parent's state machine objects
                context.WorkflowState.StateObject.StateMachinesObjects ??= new System.Collections.Generic.Dictionary<System.Guid, object>();
                context.WorkflowState.StateObject.StateMachinesObjects[subWorkflowWait.Id] = advancerResult.State;

                // Save parent sub-workflow wait states
                SaveWaitStatesToMachineState(subWorkflowWait, context.WorkflowState.StateObject);

                // Save child wait states
                SaveWaitStatesToMachineState(childWait, advancerResult.State);

                // Map parent sub-workflow to DTO
                var subWorkflowDto = _mapper.MapToDto(subWorkflowWait);

                // Check child wait type and handle cascading
                if (childWait is not CompensationWait && childWait is not SubWorkflowWait)
                {
                    // Passive wait - map to DTO and set parent reference
                    var childDto = _mapper.MapToDto(childWait);
                    childDto.ParentWaitId = subWorkflowWait.Id;

                    // Add child to parent's ChildWaits
                    subWorkflowDto.ChildWaits = new System.Collections.Generic.List<Workflows.Abstraction.DTOs.Waits.WaitInfrastructureDto> { childDto };

                    // Add parent sub-workflow to waits collection
                    context.WorkflowState.Waits.Add(subWorkflowDto);

                    // Return false - passive wait, suspend execution
                    return false;
                }
                else if (childWait is CompensationWait)
                {
                    // Active wait (compensation) - process directly
                    var childProcessor = _handlerFactory.GetProcessor(childWait);

                    // Process child wait with current context
                    // The child state is already stored in WorkflowState.StateObject.StateMachinesObjects
                    bool childContinues = await childProcessor.ProcessAsync(childWait, context);

                    // Update child state after processing
                    // (Processor may have modified the state)

                    // If child continues, we need to advance the sub-workflow again
                    // For now, return false to suspend and let orchestrator resume
                    return false;
                }
                else if (childWait is SubWorkflowWait nestedSubWorkflow)
                {
                    // Nested sub-workflow - recursively process
                    var nestedProcessor = _handlerFactory.GetProcessor(childWait);

                    // Process nested sub-workflow
                    bool nestedContinues = await nestedProcessor.ProcessAsync(childWait, context);

                    // Add parent sub-workflow to waits collection
                    context.WorkflowState.Waits.Add(subWorkflowDto);

                    return nestedContinues;
                }
                else
                {
                    // Unknown wait type
                    throw new InvalidOperationException($"Unsupported wait type in sub-workflow: {childWait.GetType().Name}");
                }
            }
            else
            {
                // Child completed immediately, continue parent execution loop
                return true;
            }
        }
    }
}
