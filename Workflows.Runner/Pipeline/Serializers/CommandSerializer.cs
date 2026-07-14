using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;
using Workflows.Primitives;

namespace Workflows.Runner.Pipeline.Serializers
{
    /// <summary>
    /// Handles CommandWaitDto objects after state machine advancement.
    /// The DTO is already enriched by the Wait -> DTO conversion; this serializer
    /// persists explicit state and appends the wait to the context.
    /// Returns true for immediate commands to keep in cache, false otherwise.
    /// </summary>
    internal class CommandSerializer : WaitSerializer
    {
        public CommandSerializer(Mapper mapper)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
        }

        public override Task<bool> Serialize(WaitInfrastructureDto yieldedWait, WorkflowExecutionContext context)
        {
            if (yieldedWait is not CommandWaitDto commandWaitDto)
            {
                throw new InvalidOperationException("CommandSerializer requires a CommandWaitDto.");
            }

            if (commandWaitDto.WaitType != WaitType.Command)
            {
                throw new InvalidOperationException("CommandSerializer requires a CommandWaitDto with WaitType.Command.");
            }

            // Save ExplicitState to WorkflowStateObject.Locals
            SaveWaitStatesToMachineState(commandWaitDto, context.WorkflowState.StateObject, context.WorkflowInstance);

            // Add to waits collection
            context.WorkflowState.Waits.Add(commandWaitDto);

            // Determine if this is a synchronous (immediate) command to keep in cache
            bool isImmediate = commandWaitDto.ExecutionMode == CommandExecutionMode.Immediate;

            return Task.FromResult(isImmediate);
        }
    }
}

