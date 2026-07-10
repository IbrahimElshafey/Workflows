using System;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Primitives;

namespace Workflows.Runner.Pipeline.CompletionChecker
{
    /// <summary>
    /// Factory for resolving type-specific wait matchers.
    /// Matchers validate incoming events against wait conditions.
    /// Matchers are created per-request since they depend on scoped WorkflowExecutionContext.
    /// </summary>
    internal class CompletionCheckerFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public CompletionCheckerFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public WaitCompletionChecker GetChecker(WaitInfrastructureDto triggeringWait)
        {
            if (triggeringWait == null)
            {
                throw new ArgumentNullException(nameof(triggeringWait));
            }

            return triggeringWait switch
            {
                SignalWaitDto _ => _serviceProvider.GetRequiredService<SignalCompletionChecker>(),
                TimeWaitDto _ => _serviceProvider.GetRequiredService<TimeWaitMatcher>(),
                CommandWaitDto _ => _serviceProvider.GetRequiredService<CommandCompletionChecker>(),
                GroupWaitDto _ => _serviceProvider.GetRequiredService<GroupCompletionChecker>(),
                SubWorkflowWaitDto _ => _serviceProvider.GetRequiredService<WorkflowCompletionChecker>(),
                _ => throw new NotSupportedException($"No matcher found for wait type: {triggeringWait.GetType().Name}")
            };
        }
    }
}
