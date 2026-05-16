using System;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Runner.Cache;
using Workflows.Runner.Pipeline.Handlers;

namespace Workflows.Runner.Pipeline
{
    /// <summary>
    /// Factory implementation for resolving type-specific handlers.
    /// </summary>
    internal class HandlerFactory
    {
        private readonly Mapper _mapper;
        private readonly WorkflowTemplateCache _templateCache;
        private readonly StateMachineAdvancer _stateMachineAdvancer;
        private readonly ICommandHandlerFactory _commandHandlerFactory;
        private readonly SignalWaitHandler _signalWaitHandler;
        private readonly TimeWaitHandler _timeWaitHandler;
        private readonly ImmediateCommandHandler _immediateCommandHandler;
        private readonly DeferredCommandHandler _deferredCommandHandler;
        private readonly GroupWaitHandler _groupWaitHandler;
        private readonly CompensationHandler _compensationHandler;

        // Note: SubWorkflowHandler needs HandlerFactory, so we delay its initialization
        private SubWorkflowHandler _subWorkflowHandler;

        public HandlerFactory(
            Mapper mapper,
            WorkflowTemplateCache templateCache,
            StateMachineAdvancer stateMachineAdvancer,
            ICommandHandlerFactory commandHandlerFactory)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _templateCache = templateCache ?? throw new ArgumentNullException(nameof(templateCache));
            _stateMachineAdvancer = stateMachineAdvancer ?? throw new ArgumentNullException(nameof(stateMachineAdvancer));
            _commandHandlerFactory = commandHandlerFactory ?? throw new ArgumentNullException(nameof(commandHandlerFactory));

            // Initialize handlers (stateless, can be reused)
            _signalWaitHandler = new SignalWaitHandler(_mapper, _templateCache);
            _timeWaitHandler = new TimeWaitHandler(_mapper);
            _immediateCommandHandler = new ImmediateCommandHandler(_commandHandlerFactory, _templateCache);
            _deferredCommandHandler = new DeferredCommandHandler(_mapper);
            _groupWaitHandler = new GroupWaitHandler(_mapper);
            _compensationHandler = new CompensationHandler(_templateCache);
        }

        public WorkflowWaitHandler GetHandler(Wait yieldedWait)
        {
            if (yieldedWait == null)
            {
                throw new ArgumentNullException(nameof(yieldedWait));
            }

            // Check for specific wait types
            if (yieldedWait is ISignalWait)
                return _signalWaitHandler;

            if (yieldedWait is TimeWait)
                return _timeWaitHandler;

            if (yieldedWait is Definition.ICommandWait commandWait)
            {
                // Determine if immediate or deferred based on execution mode
                // We need to check the DTO for execution mode, but for now assume immediate if fast
                // TODO: Add proper execution mode detection
                return _deferredCommandHandler;
            }

            if (yieldedWait is GroupWait)
                return _groupWaitHandler;

            if (yieldedWait is SubWorkflowWait)
            {
                // Lazy initialization to avoid circular dependency
                if (_subWorkflowHandler == null)
                {
                    _subWorkflowHandler = new SubWorkflowHandler(_stateMachineAdvancer, this, _mapper);
                }
                return _subWorkflowHandler;
            }

            if (yieldedWait is CompensationWait)
                return _compensationHandler;

            throw new NotSupportedException($"No handler found for wait type: {yieldedWait.GetType().Name}");
        }
    }
}
