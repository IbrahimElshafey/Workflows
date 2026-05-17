using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;
using Workflows.Runner.Cache;
using Workflows.Runner.ExpressionTransformers;

namespace Workflows.Runner.Pipeline.Processors
{
    /// <summary>
    /// Handles SignalWait objects after state machine advancement.
    /// Extracts and transforms new MatchExpression structures, updates exact-match template indexes,
    /// and appends the wait to the context. Returns false to suspend execution.
    /// </summary>
    internal class SignalWaitProcessor : WorkflowWaitProcessor
    {
        private readonly Mapper _mapper;
        private readonly MatchExpressionTransformer _matchExpressionTransformer;
        private static readonly ConcurrentDictionary<string, SignalTemplateCacheRecord> _signalTemplateCache = new();

        public SignalWaitProcessor(Mapper mapper, MatchExpressionTransformer matchExpressionTransformer)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _matchExpressionTransformer = matchExpressionTransformer ?? throw new ArgumentNullException(nameof(matchExpressionTransformer));
        }

        public override Task<bool> ProcessAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var signalWait = yieldedWait as ISignalWait;
            if (signalWait == null)
            {
                throw new InvalidOperationException("SignalWaitProcessor requires an ISignalWait.");
            }

            // Map to DTO
            var signalWaitDto = _mapper.MapToDto(yieldedWait) as SignalWaitDto;
            if (signalWaitDto == null)
            {
                throw new InvalidOperationException("Failed to map SignalWait to SignalWaitDto.");
            }

            // Extract and transform MatchExpression structures if present
            if (signalWait.MatchExpression != null && signalWaitDto.TemplateHashKey != null)
            {
                var hashKey = signalWaitDto.TemplateHashKey.ToString();

                // Check if we already have this template cached
                if (!string.IsNullOrEmpty(hashKey) && !_signalTemplateCache.ContainsKey(hashKey))
                {
                    // Cache record for template-based matching
                    // The actual match compilation happens in SignalWaitMatcher during evaluation
                    var cacheRecord = new SignalTemplateCacheRecord
                    {
                        // CompiledMatchDelegate will be set during matcher evaluation
                    };

                    _signalTemplateCache.TryAdd(hashKey, cacheRecord);
                }
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
