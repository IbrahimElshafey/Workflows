using System;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Primitives;

namespace Workflows.Runner.Pipeline.Matchers
{
    /// <summary>
    /// Factory for resolving type-specific wait matchers.
    /// Matchers validate incoming events against wait conditions.
    /// Matchers are created per-request since they depend on scoped WorkflowExecutionContext.
    /// </summary>
    internal class MatcherFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public MatcherFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public WorkflowWaitMatcher GetMatcher(WaitInfrastructureDto triggeringWait)
        {
            if (triggeringWait == null)
            {
                throw new ArgumentNullException(nameof(triggeringWait));
            }

            return triggeringWait switch
            {
                SignalWaitDto _ => _serviceProvider.GetRequiredService<SignalWaitMatcher>(),
                TimeWaitDto _ => _serviceProvider.GetRequiredService<TimeWaitMatcher>(),
                CommandWaitDto cmd when cmd.ExecutionMode == CommandExecutionMode.Deferred 
                    => _serviceProvider.GetRequiredService<DeferredCommandMatcher>(),
                GroupWaitDto _ => _serviceProvider.GetRequiredService<GroupWaitMatcher>(),
                SubWorkflowWaitDto _ => _serviceProvider.GetRequiredService<SubWorkflowWaitMatcher>(),
                _ => throw new NotSupportedException($"No matcher found for wait type: {triggeringWait.GetType().Name}")
            };
        }
    }
}
