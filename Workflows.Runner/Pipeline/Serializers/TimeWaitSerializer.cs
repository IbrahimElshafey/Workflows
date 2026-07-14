using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Serializers
{
    /// <summary>
    /// Handles TimeWaitDto objects after state machine advancement.
    /// The DTO is already enriched by the Wait -> DTO conversion; this serializer
    /// persists explicit state and appends the wait to the context.
    /// Returns false to suspend execution.
    /// </summary>
    internal class TimeWaitSerializer : WaitSerializer
    {
        public TimeWaitSerializer(Mapper mapper)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
        }

        public override Task<bool> Serialize(WaitInfrastructureDto yieldedWait, WorkflowExecutionContext context)
        {
            if (yieldedWait is not TimeWaitDto timeWaitDto)
            {
                throw new InvalidOperationException("TimeWaitSerializer requires a TimeWaitDto.");
            }

            // Save ExplicitState to WorkflowStateObject.Locals
            SaveWaitStatesToMachineState(timeWaitDto, context.WorkflowState.StateObject, context.WorkflowInstance);

            // Add to new waits
            context.WorkflowState.Waits.Add(timeWaitDto);

            // Return false - passive wait, suspend execution
            return Task.FromResult(false);
        }
    }
}

