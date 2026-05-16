using System;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Runner;
using Workflows.Primitives;
using Workflows.Runner.Cache;
using Workflows.Runner.Pipeline.Evaluators;

namespace Workflows.Runner.Pipeline
{
    /// <summary>
    /// Factory implementation for resolving type-specific evaluators.
    /// </summary>
    internal class EvaluatorFactory
    {
        private readonly WorkflowTemplateCache _templateCache;
        private readonly IWorkflowRegistry _workflowRegistry;
        private readonly SignalWaitEvaluator _signalWaitEvaluator;
        private readonly TimeWaitEvaluator _timeWaitEvaluator;
        private readonly DeferredCommandEvaluator _deferredCommandEvaluator;
        private readonly GroupWaitEvaluator _groupWaitEvaluator;

        public EvaluatorFactory(
            WorkflowTemplateCache templateCache,
            IWorkflowRegistry workflowRegistry)
        {
            _templateCache = templateCache ?? throw new ArgumentNullException(nameof(templateCache));
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));

            // Initialize evaluators (stateless, can be reused)
            _signalWaitEvaluator = new SignalWaitEvaluator(_templateCache, _workflowRegistry);
            _timeWaitEvaluator = new TimeWaitEvaluator();
            _deferredCommandEvaluator = new DeferredCommandEvaluator(_templateCache);
            _groupWaitEvaluator = new GroupWaitEvaluator();
        }

        public WorkflowWaitEvaluator GetEvaluator(WaitInfrastructureDto triggeringWait)
        {
            if (triggeringWait == null)
            {
                throw new ArgumentNullException(nameof(triggeringWait));
            }

            return triggeringWait switch
            {
                SignalWaitDto _ => _signalWaitEvaluator,
                TimeWaitDto _ => _timeWaitEvaluator,
                CommandWaitDto cmd when cmd.ExecutionMode == CommandExecutionMode.DeferredCommand 
                    => _deferredCommandEvaluator,
                GroupWaitDto _ => _groupWaitEvaluator,
                _ => throw new NotSupportedException($"No evaluator found for wait type: {triggeringWait.GetType().Name}")
            };
        }
    }
}
