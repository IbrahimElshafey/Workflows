using System;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Runner;
using Workflows.Primitives;

namespace Workflows.Runner.Pipeline.Matchers
{
    /// <summary>
    /// Factory for resolving type-specific wait matchers.
    /// Matchers validate incoming events against wait conditions.
    /// </summary>
    internal class MatcherFactory
    {
        private readonly IWorkflowRegistry _workflowRegistry;
        private readonly SignalWaitMatcher _signalWaitMatcher;
        private readonly TimeWaitMatcher _timeWaitMatcher;
        private readonly DeferredCommandMatcher _deferredCommandMatcher;
        private readonly GroupWaitMatcher _groupWaitMatcher;

        public MatcherFactory(IWorkflowRegistry workflowRegistry)
        {
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));

            // Initialize matchers (stateless, can be reused)
            _signalWaitMatcher = new SignalWaitMatcher(_workflowRegistry);
            _timeWaitMatcher = new TimeWaitMatcher();
            _deferredCommandMatcher = new DeferredCommandMatcher();
            _groupWaitMatcher = new GroupWaitMatcher();
        }

        public WorkflowWaitMatcher GetMatcher(WaitInfrastructureDto triggeringWait)
        {
            if (triggeringWait == null)
            {
                throw new ArgumentNullException(nameof(triggeringWait));
            }

            return triggeringWait switch
            {
                SignalWaitDto _ => _signalWaitMatcher,
                TimeWaitDto _ => _timeWaitMatcher,
                CommandWaitDto cmd when cmd.ExecutionMode == CommandExecutionMode.DeferredCommand 
                    => _deferredCommandMatcher,
                GroupWaitDto _ => _groupWaitMatcher,
                _ => throw new NotSupportedException($"No matcher found for wait type: {triggeringWait.GetType().Name}")
            };
        }
    }
}
