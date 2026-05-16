using System;
using System.Threading.Tasks;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Handlers
{
    /// <summary>
    /// Handles TimeWait objects after state machine advancement.
    /// Calculates absolute target datetime offsets and registers them into the context for scheduling.
    /// Returns false to suspend execution.
    /// </summary>
    internal class TimeWaitHandler : WorkflowWaitHandler
    {
        private readonly Mapper _mapper;

        public TimeWaitHandler(Mapper mapper)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public override Task<bool> HandleAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var timeWait = yieldedWait as TimeWait;
            if (timeWait == null)
            {
                throw new InvalidOperationException("TimeWaitHandler requires a TimeWait.");
            }

            // TODO: Calculate absolute target datetime offsets and register for scheduling

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
