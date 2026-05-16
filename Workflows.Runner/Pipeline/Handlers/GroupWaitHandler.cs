using System;
using System.Threading.Tasks;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Handlers
{
    /// <summary>
    /// Handles GroupWait objects.
    /// Unfolds composite layers and asserts that ChildWaitsRuntime contains only IPassiveWait references,
    /// throwing structural errors if active intents are nested inside a parallel pool.
    /// Returns false to suspend execution.
    /// </summary>
    internal class GroupWaitHandler : WorkflowWaitHandler
    {
        private readonly Mapper _mapper;

        public GroupWaitHandler(Mapper mapper)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public override Task<bool> HandleAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var groupWait = yieldedWait as GroupWait;
            if (groupWait == null)
            {
                throw new InvalidOperationException("GroupWaitHandler requires a GroupWait.");
            }

            // TODO: Unfold composite layers
            // TODO: Assert that ChildWaitsRuntime contains only IPassiveWait references

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
