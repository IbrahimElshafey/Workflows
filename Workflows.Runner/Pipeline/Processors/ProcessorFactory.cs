using System;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Runner.ExpressionTransformers;

namespace Workflows.Runner.Pipeline.Processors
{
    /// <summary>
    /// Factory for resolving type-specific wait processors.
    /// Processors handle yielded waits after state machine advancement.
    /// </summary>
    internal class ProcessorFactory
    {
        private readonly Mapper _mapper;
        private readonly StateMachineAdvancer _stateMachineAdvancer;
        private readonly ICommandHandlerFactory _commandHandlerFactory;
        private readonly MatchExpressionTransformer _matchExpressionTransformer;
        private readonly SignalWaitProcessor _signalWaitProcessor;
        private readonly TimeWaitProcessor _timeWaitProcessor;
        private readonly ImmediateCommandProcessor _immediateCommandProcessor;
        private readonly DeferredCommandProcessor _deferredCommandProcessor;
        private readonly GroupWaitProcessor _groupWaitProcessor;
        private readonly CompensationProcessor _compensationProcessor;

        // Note: SubWorkflowProcessor needs ProcessorFactory, so we delay its initialization
        private SubWorkflowProcessor _subWorkflowProcessor;

        public ProcessorFactory(
            Mapper mapper,
            StateMachineAdvancer stateMachineAdvancer,
            ICommandHandlerFactory commandHandlerFactory,
            MatchExpressionTransformer matchExpressionTransformer)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _stateMachineAdvancer = stateMachineAdvancer ?? throw new ArgumentNullException(nameof(stateMachineAdvancer));
            _commandHandlerFactory = commandHandlerFactory ?? throw new ArgumentNullException(nameof(commandHandlerFactory));
            _matchExpressionTransformer = matchExpressionTransformer ?? throw new ArgumentNullException(nameof(matchExpressionTransformer));

            // Initialize processors (stateless, can be reused)
            _signalWaitProcessor = new SignalWaitProcessor(_mapper, _matchExpressionTransformer);
            _timeWaitProcessor = new TimeWaitProcessor(_mapper);
            _immediateCommandProcessor = new ImmediateCommandProcessor(_commandHandlerFactory);
            _deferredCommandProcessor = new DeferredCommandProcessor(_mapper);
            _groupWaitProcessor = new GroupWaitProcessor(_mapper, _stateMachineAdvancer);
            _groupWaitProcessor.ProcessorFactory = this;
            _compensationProcessor = new CompensationProcessor();
        }

        public WorkflowWaitProcessor GetProcessor(Wait yieldedWait)
        {
            if (yieldedWait == null)
            {
                throw new ArgumentNullException(nameof(yieldedWait));
            }

            // Check for specific wait types
            if (yieldedWait is ISignalWait)
                return _signalWaitProcessor;

            if (yieldedWait is TimeWait)
                return _timeWaitProcessor;

            if (yieldedWait.WaitType == Workflows.Primitives.WaitType.Command)
            {
                // Determine if immediate or deferred based on execution mode
                var commandWaitType = yieldedWait.GetType();
                var executionModeProperty = commandWaitType.GetProperty("ExecutionMode",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                if (executionModeProperty != null)
                {
                    var executionMode = executionModeProperty.GetValue(yieldedWait);
                    if (executionMode != null && executionMode.ToString() == "Immediate")
                    {
                        return _immediateCommandProcessor;
                    }
                }

                // Default to deferred for backward compatibility
                return _deferredCommandProcessor;
            }

            if (yieldedWait is GroupWait)
                return _groupWaitProcessor;

            if (yieldedWait is SubWorkflowWait)
            {
                // Lazy initialization to avoid circular dependency
                if (_subWorkflowProcessor == null)
                {
                    _subWorkflowProcessor = new SubWorkflowProcessor(_stateMachineAdvancer, this, _mapper);
                }
                return _subWorkflowProcessor;
            }

            if (yieldedWait is CompensationWait)
                return _compensationProcessor;

            throw new NotSupportedException($"No processor found for wait type: {yieldedWait.GetType().Name}");
        }
    }
}
