using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Runner;
using Workflows.Runner.Cache;

namespace Workflows.Runner.Pipeline.Matchers
{
    /// <summary>
    /// Matches incoming signal events against SignalWait constraints.
    /// Works purely with DTOs - no Wait object conversion needed.
    /// </summary>
    internal class SignalWaitMatcher : WorkflowWaitMatcher
    {
        private readonly IWorkflowRegistry _workflowRegistry;
        private readonly WorkflowExecutionContext _context;
        private readonly MatcherFactory _matcherFactory;
        private static readonly ConcurrentDictionary<string, SignalTemplateCacheRecord> _signalCache = new();

        public SignalWaitMatcher(
            IWorkflowRegistry workflowRegistry, 
            WorkflowExecutionContext context,
            MatcherFactory matcherFactory)
        {
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _matcherFactory = matcherFactory ?? throw new ArgumentNullException(nameof(matcherFactory));
        }

        public override async Task<bool> MatchAsync(WaitInfrastructureDto waitDto)
        {
            var signalWaitDto = waitDto as SignalWaitDto;
            if (signalWaitDto == null)
            {
                throw new InvalidOperationException("SignalWaitMatcher requires a SignalWaitDto.");
            }

            var signal = _context.Signal;
            if (signal == null)
            {
                return false; // No signal payload
            }

            // Validate signal identifier match
            if (!string.Equals(signalWaitDto.SignalIdentifier, signal.SignalIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return false; // Signal identifier mismatch
            }

            // Get or compile the match expression from cache if available
            var compiledMatch = GetOrBuildCompiledMatch(signalWaitDto);
            if (compiledMatch != null)
            {
                // Evaluate match expression using DTO data
                // TODO: We need ExplicitState from DTO to evaluate match
                // For now, skip match evaluation until we add ExplicitState to DTO
                // bool matchResult = compiledMatch(signal.Data, _context.WorkflowInstance, explicitState);
                // if (!matchResult)
                // {
                //     return false; // Match expression failed
                // }
            }

            // TODO: Execute AfterMatchAction if we store it in DTO

            // Mark this wait as completed
            signalWaitDto.Status = WaitStatus.Completed;

            // Propagate matching to parent wait (e.g., GroupWait or SubWorkflowWait) if present
            if (signalWaitDto.ParentWaitId.HasValue)
            {
                return await MatchParentAsync(signalWaitDto.ParentWaitId.Value, _context, _matcherFactory);
            }

            return true;
        }

        private Func<object, object, object, bool> GetOrBuildCompiledMatch(SignalWaitDto dto)
        {
            if (dto.TemplateHashKey is string hashKey)
            {
                if (_signalCache.TryGetValue(hashKey, out var cached) && cached?.CompiledMatchDelegate != null)
                {
                    return cached.CompiledMatchDelegate;
                }
            }

            // For now, return null - match compilation will be added later
            return null;
        }
    }
}
