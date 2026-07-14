using System;
using Workflows.Abstraction.DTOs.Waits;
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

        public WaitSerializer GetSerializer(WaitInfrastructureDto yieldedWait)
        {
            if (yieldedWait == null)
            {
                throw new ArgumentNullException(nameof(yieldedWait));
            }

            // Check for specific wait DTO types
            return yieldedWait switch
            {
                SignalWaitDto _ => _signalWaitSerializer,
                TimeWaitDto _ => _timeWaitSerializer,
                CommandWaitDto _ => _deferredCommandSerializer,
                GroupWaitDto _ => _groupWaitSerializer,
                ExternalGroupWaitDto _ => _externalGroupWaitSerializer,
                SubWorkflowWaitDto _ => _groupWaitSerializer, // Sub-workflow DTOs are handled by group serializer logic
                CompensationWaitDto _ => _compensationSerializer,
                _ => throw new NotSupportedException($"No serializer found for wait DTO type: {yieldedWait.GetType().Name}")
            };
        }
    }
}
