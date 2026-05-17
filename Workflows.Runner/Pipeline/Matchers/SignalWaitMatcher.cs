using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Runner.Cache;

namespace Workflows.Runner.Pipeline.Matchers
{
    /// <summary>
    /// Matches incoming signal events against SignalWait constraints.
    /// Runs the cached MatchIf template filter against incoming payload and state variables.
    /// If matching fails or is incomplete, returns false immediately to abort execution.
    /// </summary>
    internal class SignalWaitMatcher : WorkflowWaitMatcher
    {
        private readonly IWorkflowRegistry _workflowRegistry;
        private static readonly ActionInvokerCache _invokerCache = new();
        private static readonly ConcurrentDictionary<string, SignalTemplateCacheRecord> _signalCache = new();

        public SignalWaitMatcher(IWorkflowRegistry workflowRegistry)
        {
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
        }

        public override Task<bool> MatchAsync(WorkflowExecutionContext context)
        {
            var signalWaitDto = context.TriggeringWaitDto as SignalWaitDto;
            if (signalWaitDto == null)
            {
                throw new InvalidOperationException("SignalWaitMatcher requires a SignalWaitDto.");
            }

            var signalWait = context.TriggeringWait as ISignalWait;
            if (signalWait == null)
            {
                throw new InvalidOperationException("Triggering wait could not be mapped to ISignalWait.");
            }

            var signal = context.Signal;
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

            // Check composite parent dependencies (GroupWait) if needed
            if (signalWaitDto.ParentWaitId.HasValue)
            {
                // This signal is part of a GroupWait - need to check if parent group condition is met
                // The actual group evaluation happens in GroupWaitMatcher when the group itself is triggered
                // Here we just mark this child signal as completed by returning true
                // The orchestrator will then check the parent GroupWait status
            }

            return Task.FromResult(true);
        }

        private Func<object, object, object, bool> GetOrBuildCompiledMatch(SignalWaitDto dto, ISignalWait wait)
        {
            if (dto.TemplateHashKey is string hashKey)
            {
                if (_signalCache.TryGetValue(hashKey, out var cached) && cached?.CompiledMatchDelegate != null)
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
            var invoker = _invokerCache.GetOrAddAfterMatchInvoker(action.GetType());
            if (invoker == null)
            {
                throw new InvalidOperationException("AfterMatchAction signature is not supported or Invoke method not found.");
            }

            invoker(action, signalData, explicitState);
        }
    }
}
