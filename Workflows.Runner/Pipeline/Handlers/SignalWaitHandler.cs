using System;
using System.Threading.Tasks;
using Workflows.Definition;
using Workflows.Runner.Cache;

namespace Workflows.Runner.Pipeline.Handlers
{
    /// <summary>
    /// Handles SignalWait objects after state machine advancement.
    /// Extracts and transforms new MatchExpression structures, updates exact-match template indexes,
    /// and appends the wait to the context. Returns false to suspend execution.
    /// </summary>
    internal class SignalWaitHandler : WorkflowWaitHandler
    {
        private readonly Mapper _mapper;
        private readonly WorkflowTemplateCache _templateCache;

        public SignalWaitHandler(Mapper mapper, WorkflowTemplateCache templateCache)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _templateCache = templateCache ?? throw new ArgumentNullException(nameof(templateCache));
        }

        public override Task<bool> HandleAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var signalWait = yieldedWait as ISignalWait;
            if (signalWait == null)
            {
                throw new InvalidOperationException("SignalWaitHandler requires an ISignalWait.");
            }

            // TODO: Extract and transform MatchExpression structures using MatchExpressionTransformer
            // TODO: Update exact-match template indexes

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
