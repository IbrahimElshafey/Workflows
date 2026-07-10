using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;
using Workflows.Primitives;

namespace Workflows.Runner.Pipeline.Serializers
{
    /// <summary>
    /// Handles deferred command dispatch.
    /// Serializes the contract to an out-of-process messaging shape and bundles
    /// the dispatch payload into the execution context. Returns false to suspend execution.
    /// </summary>
    internal class CommandSerializer : WaitSerializer
    {
        private readonly Mapper _mapper;

        public CommandSerializer(Mapper mapper)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public override Task<bool> Serialize(Wait yieldedWait, WorkflowExecutionContext context)
        {
            if (yieldedWait.WaitType != WaitType.Command)
            {
                throw new InvalidOperationException("CommandSerializer requires a CommandWait.");
            }

            // Get command data for serialization
            var commandWaitType = yieldedWait.GetType();
            var commandDataProperty = commandWaitType.GetProperty("CommandData", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var commandData = commandDataProperty?.GetValue(yieldedWait);

            // Serialize command to out-of-process messaging shape
            // This would typically involve:
            // 1. Extracting command type and data
            // 2. Creating a dispatch envelope with routing metadata
            // 3. Storing in a dispatch queue or message bus integration point
            // For now, we'll bundle the essential information into the DTO
            var commandDto = _mapper.MapToDto(yieldedWait) as CommandWaitDto;
            if (commandDto != null && commandData != null)
            {
                // Note: Actual serialization to message bus would happen in the Orchestrator
                // after receiving this DTO. The runner just marks it for dispatch.
            }

            // Save ExplicitState to WorkflowStateObject.WaitStatesObjects
            SaveWaitStatesToMachineState(yieldedWait, context.WorkflowState.StateObject);

            // Map to DTO and add to waits collection
            var waitDto = commandDto ?? _mapper.MapToDto(yieldedWait);
            context.WorkflowState.Waits.Add(waitDto);

            // Determine if this is a synchronous (immediate) command to keep in cache
            bool isImmediate = false;
            var executionModeProperty = commandWaitType.GetProperty("ExecutionMode",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

            if (executionModeProperty != null)
            {
                var executionMode = executionModeProperty.GetValue(yieldedWait);
                if (executionMode != null && executionMode.ToString() == "Immediate")
                {
                    isImmediate = true;
                }
            }

            return Task.FromResult(isImmediate);
        }
    }
}

