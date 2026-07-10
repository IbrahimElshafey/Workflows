using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;
using Workflows.Primitives;

namespace Workflows.Runner.Pipeline.Processors
{
    /// <summary>
    /// Handles deferred command dispatch.
    /// Serializes the contract to an out-of-process messaging shape and bundles
    /// the dispatch payload into the execution context. Returns false to suspend execution.
    /// </summary>
    internal class CommandProcessor : WorkflowWaitProcessor
    {
        private readonly Mapper _mapper;

        public CommandProcessor(Mapper mapper)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public override Task<bool> ProcessAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            if (yieldedWait.WaitType != WaitType.Command)
            {
                throw new InvalidOperationException("CommandProcessor requires a CommandWait.");
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

            // Return false - passive wait, suspend execution until callback
            return Task.FromResult(false);
        }
    }
}

