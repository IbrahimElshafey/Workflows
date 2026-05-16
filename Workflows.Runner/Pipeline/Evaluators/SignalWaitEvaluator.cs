using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Runner.Cache;

namespace Workflows.Runner.Pipeline.Evaluators
{
    /// <summary>
    /// Evaluates incoming signal events against SignalWait constraints.
    /// Runs the cached MatchIf template filter against incoming payload and state variables.
    /// If evaluation fails or is incomplete, returns false immediately to abort execution.
    /// </summary>
    internal class SignalWaitEvaluator : WorkflowWaitEvaluator
    {
        private readonly WorkflowTemplateCache _templateCache;
        private readonly IWorkflowRegistry _workflowRegistry;

        public SignalWaitEvaluator(
            WorkflowTemplateCache templateCache,
            IWorkflowRegistry workflowRegistry)
        {
            _templateCache = templateCache ?? throw new ArgumentNullException(nameof(templateCache));
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
        }

        public override Task<bool> EvaluateAsync(WorkflowExecutionContext context)
        {
            var signalWaitDto = context.TriggeringWaitDto as SignalWaitDto;
            if (signalWaitDto == null)
            {
                throw new InvalidOperationException("SignalWaitEvaluator requires a SignalWaitDto.");
            }

            var signalWait = context.TriggeringWait as ISignalWait;
            if (signalWait == null)
            {
                throw new InvalidOperationException("Triggering wait could not be mapped to ISignalWait.");
            }

            var signal = context.IncomingRequest.Signal;
            if (signal == null)
            {
                return Task.FromResult(false); // No signal payload
            }

            // Validate signal identifier match
            if (!string.Equals(signalWaitDto.SignalIdentifier, signal.SignalIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(false); // Signal identifier mismatch
            }

            // Get or compile the match expression
            var compiledMatch = GetOrBuildCompiledMatch(signalWaitDto, signalWait);
            if (compiledMatch != null)
            {
                // Evaluate match expression
                bool matchResult = compiledMatch(signal.Data, context.WorkflowInstance, signalWait.ExplicitState);
                if (!matchResult)
                {
                    return Task.FromResult(false); // Match expression failed
                }
            }

            // Execute AfterMatchAction if present
            var afterMatchAction = signalWait.AfterMatchAction;
            if (afterMatchAction != null)
            {
                InvokeAfterMatchAction(afterMatchAction, signal.Data, signalWait.ExplicitState);
            }

            // TODO: Check composite parent dependencies (GroupWait) if needed

            return Task.FromResult(true);
        }

        private Func<object, object, object, bool> GetOrBuildCompiledMatch(SignalWaitDto dto, ISignalWait wait)
        {
            if (dto.TemplateHashKey is string hashKey)
            {
                var cached = _templateCache.GetSignal(hashKey);
                if (cached?.CompiledMatchDelegate != null)
                {
                    return cached.CompiledMatchDelegate;
                }
            }

            if (wait.MatchExpression == null)
            {
                return null;
            }

            // Get signal type from registry
            var signalType = _workflowRegistry.SignalTypes.TryGetValue(dto.SignalIdentifier, out var type)
                ? type
                : typeof(object);

            // TODO: Compile match expression (moved from WorkflowRunner)
            // var compiled = CompileMatch(wait.MatchExpression, signalType);

            // For now, return null until we move the compile logic
            return null;
        }

        private void InvokeAfterMatchAction(object action, object signalData, object explicitState)
        {
            var invoker = _templateCache.GetOrAddAfterMatchInvoker(action.GetType());
            if (invoker == null)
            {
                throw new InvalidOperationException("AfterMatchAction signature is not supported or Invoke method not found.");
            }

            invoker(action, signalData, explicitState);
        }
    }
}
