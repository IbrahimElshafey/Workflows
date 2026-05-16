using System;
using System.Threading.Tasks;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Handlers
{
    /// <summary>
    /// Handles deferred command dispatch.
    /// Serializes the contract to an out-of-process messaging shape and bundles
    /// the dispatch payload into the execution context. Returns false to suspend execution.
    /// </summary>
    internal class DeferredCommandHandler : WorkflowWaitHandler
    {
        private readonly Mapper _mapper;

        public DeferredCommandHandler(Mapper mapper)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public override Task<bool> HandleAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var commandWait = yieldedWait as Definition.ICommandWait;
            if (commandWait == null)
            {
                throw new InvalidOperationException("DeferredCommandHandler requires an ICommandWait.");
            }

            // TODO: Serialize command to out-of-process messaging shape
            // TODO: Bundle dispatch payload into execution context

            // Save ExplicitState to WorkflowStateObject.WaitStatesObjects
            SaveWaitStatesToMachineState(yieldedWait, context.ActiveState);

            // Map to DTO and add to new waits
            var waitDto = _mapper.MapToDto(yieldedWait);
            context.NewWaits.Add(waitDto);

            // Return false - passive wait, suspend execution
            return Task.FromResult(false);
        }
    }
}
