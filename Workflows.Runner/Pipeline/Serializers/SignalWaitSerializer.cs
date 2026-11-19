using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;
using Workflows.Runner.Cache;
using Workflows.Runner.ExpressionTransformers;

namespace Workflows.Runner.Pipeline.Serializers
{
    /// <summary>
    /// Handles SignalWaitDto objects after state machine advancement.
    /// The DTO is already enriched by the Wait -> DTO conversion; this serializer
    /// persists explicit state and appends the wait to the context.
    /// Returns false to suspend execution.
    /// </summary>
    internal class SignalWaitSerializer : WaitSerializer
    {
        public SignalWaitSerializer(Mapper mapper)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
        }

        public override Task<bool> Serialize(WaitInfrastructureDto yieldedWait, WorkflowExecutionContext context)
        {
            if (yieldedWait is not SignalWaitDto signalWaitDto)
            {
                throw new InvalidOperationException("SignalWaitSerializer requires a SignalWaitDto.");
            }

            // Save ExplicitState to WorkflowStateObject.Locals
            SaveWaitStatesToMachineState(signalWaitDto, context.WorkflowState.StateObject, context.WorkflowInstance);

            // Add to new waits
            context.WorkflowState.Waits.Add(signalWaitDto);

            // Return false - passive wait, suspend execution
            return Task.FromResult(false);
        }
    }
}
