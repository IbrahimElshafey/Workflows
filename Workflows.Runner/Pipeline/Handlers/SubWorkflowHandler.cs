using System;
using System.Threading.Tasks;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Handlers
{
    /// <summary>
    /// Handles SubWorkflowWait objects.
    /// Extracts the target child IAsyncEnumerable stream, drives the sub-workflow's initial entry loop
    /// to extract its first inner yielded Wait primitive, and hands that initial child wait down to
    /// its matching specific handler. Returns child continuation outcome (true/false).
    /// </summary>
    internal class SubWorkflowHandler : WorkflowWaitHandler
    {
        private readonly StateMachineAdvancer _stateMachineAdvancer;
        private readonly HandlerFactory _handlerFactory;
        private readonly Mapper _mapper;

        public SubWorkflowHandler(
            StateMachineAdvancer stateMachineAdvancer,
            HandlerFactory handlerFactory,
            Mapper mapper)
        {
            _stateMachineAdvancer = stateMachineAdvancer ?? throw new ArgumentNullException(nameof(stateMachineAdvancer));
            _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public override async Task<bool> HandleAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var subWorkflowWait = yieldedWait as SubWorkflowWait;
            if (subWorkflowWait == null)
            {
                throw new InvalidOperationException("SubWorkflowHandler requires a SubWorkflowWait.");
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
                context.ActiveState.StateMachinesObjects ??= new System.Collections.Generic.Dictionary<System.Guid, object>();
                context.ActiveState.StateMachinesObjects[subWorkflowWait.Id] = advancerResult.State;

                // Save parent sub-workflow wait states
                SaveWaitStatesToMachineState(subWorkflowWait, context.ActiveState);

                // Map parent sub-workflow to DTO
                var subWorkflowDto = _mapper.MapToDto(subWorkflowWait);

                // Map child wait to DTO and set parent reference
                var childDto = _mapper.MapToDto(childWait);
                childDto.ParentWaitId = subWorkflowWait.Id;

                // Add child to parent's ChildWaits
                subWorkflowDto.ChildWaits = new System.Collections.Generic.List<Workflows.Abstraction.DTOs.Waits.WaitInfrastructureDto> { childDto };

                // Add parent sub-workflow to new waits
                context.NewWaits.Add(subWorkflowDto);

                // Get the appropriate handler for the child wait and cascade the call
                var childHandler = _handlerFactory.GetHandler(childWait);

                // Note: For cascading handlers, we should use the same context
                // The child handler will add to the parent context's NewWaits list
                // For now, we return false to indicate passive wait behavior
                // TODO: Implement proper handler cascading when needed
                return false;
            }
            else
            {
                // Child completed immediately, continue parent execution loop
                return true;
            }
        }
    }
}
