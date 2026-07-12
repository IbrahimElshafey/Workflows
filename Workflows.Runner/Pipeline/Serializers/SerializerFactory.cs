using System;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Runner.ExpressionTransformers;

namespace Workflows.Runner.Pipeline.Serializers
{
    /// <summary>
    /// Factory for resolving type-specific wait serializers.
    /// Serializers handle yielded waits after state machine advancement.
    /// </summary>
    internal class SerializerFactory
    {
        private readonly Mapper _mapper;
        private readonly StateMachineAdvancer _stateMachineAdvancer;
        private readonly ICommandHandlerFactory _commandHandlerFactory;
        private readonly MatchExpressionTransformer _matchExpressionTransformer;
        private readonly SignalWaitSerializer _signalWaitSerializer;
        private readonly TimeWaitSerializer _timeWaitSerializer;
        private readonly CommandSerializer _deferredCommandSerializer;
        private readonly GroupWaitSerializer _groupWaitSerializer;
        private readonly ExternalGroupWaitSerializer _externalGroupWaitSerializer;
        private readonly CompensationWaitSerializer _compensationSerializer;

        public SerializerFactory(
            Mapper mapper,
            StateMachineAdvancer stateMachineAdvancer,
            ICommandHandlerFactory commandHandlerFactory,
            MatchExpressionTransformer matchExpressionTransformer)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _stateMachineAdvancer = stateMachineAdvancer ?? throw new ArgumentNullException(nameof(stateMachineAdvancer));
            _commandHandlerFactory = commandHandlerFactory ?? throw new ArgumentNullException(nameof(commandHandlerFactory));
            _matchExpressionTransformer = matchExpressionTransformer ?? throw new ArgumentNullException(nameof(matchExpressionTransformer));

            // Initialize serializers (stateless, can be reused)
            _signalWaitSerializer = new SignalWaitSerializer(_mapper);
            _timeWaitSerializer = new TimeWaitSerializer(_mapper);
            _deferredCommandSerializer = new CommandSerializer(_mapper);
            _groupWaitSerializer = new GroupWaitSerializer(_mapper, _stateMachineAdvancer);
            _groupWaitSerializer.ProcessorFactory = this;
            _externalGroupWaitSerializer = new ExternalGroupWaitSerializer(_mapper, _stateMachineAdvancer);
            _externalGroupWaitSerializer.ProcessorFactory = this;
            _compensationSerializer = new CompensationWaitSerializer(_mapper);
        }

        public WaitSerializer GetSerializer(Wait yieldedWait)
        {
            if (yieldedWait == null)
            {
                throw new ArgumentNullException(nameof(yieldedWait));
            }

            // Check for specific wait types
            if (yieldedWait is ISignalWait)
                return _signalWaitSerializer;

            if (yieldedWait is TimeWait)
                return _timeWaitSerializer;

            if (yieldedWait.WaitType == Workflows.Primitives.WaitType.Command)
            {
                // All commands are now serialized the same way (defer to CommandSerializer)
                return _deferredCommandSerializer;
            }

            if (yieldedWait is GroupWait)
                return _groupWaitSerializer;

            if (yieldedWait is ExternalGroupWait)
                return _externalGroupWaitSerializer;

            if (yieldedWait is CompensationWait)
                return _compensationSerializer;

            throw new NotSupportedException($"No serializer found for wait type: {yieldedWait.GetType().Name}");
        }
    }
}
