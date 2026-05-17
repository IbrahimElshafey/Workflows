using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Processors
{
    /// <summary>
    /// Handles TimeWait objects after state machine advancement.
    /// Calculates absolute target datetime offsets and registers them into the context for scheduling.
    /// Returns false to suspend execution.
    /// </summary>
    internal class TimeWaitProcessor : WorkflowWaitProcessor
    {
        private readonly Mapper _mapper;

        public TimeWaitProcessor(Mapper mapper)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public override Task<bool> ProcessAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var timeWait = yieldedWait as TimeWait;
            if (timeWait == null)
            {
                throw new InvalidOperationException("TimeWaitProcessor requires a TimeWait.");
            }

            // Calculate absolute target datetime offsets and register for scheduling
            var timeWaitDto = _mapper.MapToDto(yieldedWait) as TimeWaitDto;
            if (timeWaitDto != null)
            {
                // Calculate absolute target time based on TimeToWait offset
                // The actual FiredAt calculation happens in the mapper
                // Here we just ensure the DTO is properly prepared for scheduling

                // Note: Actual scheduling registration happens in the Orchestrator
                // when it persists this DTO and sets up timer triggers
            }

            // Save ExplicitState to WorkflowStateObject.WaitStatesObjects
            SaveWaitStatesToMachineState(yieldedWait, context.ActiveState);

            // Map to DTO and add to new waits
            var waitDto = timeWaitDto ?? _mapper.MapToDto(yieldedWait);
            context.NewWaits.Add(waitDto);

            // Return false - passive wait, suspend execution
            return Task.FromResult(false);
        }
    }
}

