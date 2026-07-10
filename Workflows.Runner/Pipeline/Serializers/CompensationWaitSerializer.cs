using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Serializers
{
    /// <summary>
    /// Handles CompensationWait objects by serializing them and suspending execution.
    /// Actual compensation actions are executed in LIFO order by the orchestrator/database layer.
    /// </summary>
    internal class CompensationWaitSerializer : WaitSerializer
    {
        private readonly Mapper _mapper;

        public CompensationWaitSerializer(Mapper mapper)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public override Task<bool> Serialize(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var compensationWait = yieldedWait as CompensationWait;
            if (compensationWait == null)
            {
                throw new InvalidOperationException("CompensationWaitSerializer requires a CompensationWait.");
            }

            SaveWaitStatesToMachineState(yieldedWait, context.WorkflowState.StateObject);

            var waitDto = _mapper.MapToDto(yieldedWait);
            context.WorkflowState.Waits.Add(waitDto);

            return Task.FromResult(false); // Suspend execution, do not keep in cache
        }
    }
}
