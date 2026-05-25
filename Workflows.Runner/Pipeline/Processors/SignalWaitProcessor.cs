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
            if (signalWait.MatchExpression != null && !string.IsNullOrEmpty(signalWaitDto.TemplateHashKey))
            {
                var hashKey = signalWaitDto.TemplateHashKey;
                if (!Matchers.SignalWaitMatcher.SignalCache.ContainsKey(hashKey))
                {
                    var transformResult = _matchExpressionTransformer.Transform(signalWait.MatchExpression, context.WorkflowInstance);

                    Func<object, object, string[]> compiledInstanceExpr = null;
                    if (transformResult.InstanceExactMatchExpression != null)
                    {
                        var compiler = new ExpressionCompiler();
                        compiledInstanceExpr = compiler.CompiledInstanceExactMatchExpression(transformResult.InstanceExactMatchExpression);
                    }

                    Func<object, object, object, bool> compiledMatch = null;
                    if (transformResult.MatchExpression != null)
                    {
                        var compiler = new ExpressionCompiler();
                        compiledMatch = compiler.CompiledMatchExpression(transformResult.MatchExpression);
                    }

                    var record = new SignalTemplateCacheRecord
                    {
                        CompiledMatchDelegate = compiledMatch,
                        CompiledInstanceExactMatchExpression = compiledInstanceExpr
                    };

                    Matchers.SignalWaitMatcher.SignalCache.TryAdd(hashKey, record);
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
