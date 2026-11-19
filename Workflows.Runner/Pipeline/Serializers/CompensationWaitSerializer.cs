using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Serializers
{
    /// <summary>
    /// Handles CompensationWaitDto objects by serializing them and suspending execution.
    /// Actual compensation actions are executed in LIFO order by the orchestrator/database layer.
    /// </summary>
    internal class CompensationWaitSerializer : WaitSerializer
    {
        public CompensationWaitSerializer(Mapper mapper)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
        }

        public override Task<bool> Serialize(WaitInfrastructureDto yieldedWait, WorkflowExecutionContext context)
        {
            if (yieldedWait is not CompensationWaitDto compensationWaitDto)
            {
                throw new InvalidOperationException("CompensationWaitSerializer requires a CompensationWaitDto.");
            }

            SaveWaitStatesToMachineState(compensationWaitDto, context.WorkflowState.StateObject, context.WorkflowInstance);

            context.WorkflowState.Waits.Add(compensationWaitDto);

            return Task.FromResult(false); // Suspend execution, do not keep in cache
        }
    }
}
