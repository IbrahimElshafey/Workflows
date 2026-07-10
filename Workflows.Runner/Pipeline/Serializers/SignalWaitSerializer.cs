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
    /// Handles SignalWait objects after state machine advancement.
    /// Extracts and transforms new MatchExpression structures, updates exact-match template indexes,
    /// and appends the wait to the context. Returns false to suspend execution.
    /// </summary>
    internal class SignalWaitSerializer : WaitSerializer
    {
        private readonly Mapper _mapper;

        public SignalWaitSerializer(Mapper mapper)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public override Task<bool> Serialize(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var signalWait = yieldedWait as ISignalWait;
            if (signalWait == null)
            {
                throw new InvalidOperationException("SignalWaitSerializer requires an ISignalWait.");
            }

            // Map to DTO
            var signalWaitDto = _mapper.MapToDto(yieldedWait) as SignalWaitDto;
            if (signalWaitDto == null)
            {
                throw new InvalidOperationException("Failed to map SignalWait to SignalWaitDto.");
            }

            // Save ExplicitState to WorkflowStateObject.WaitStatesObjects
            SaveWaitStatesToMachineState(yieldedWait, context.WorkflowState.StateObject);

            // Add to new waits
            context.WorkflowState.Waits.Add(signalWaitDto);

            // Return false - passive wait, suspend execution
            return Task.FromResult(false);
        }
    }
}
