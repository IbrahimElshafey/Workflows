using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Primitives;
using Workflows.Runner.Cache;

namespace Workflows.Runner.Tests.Infrastructure
{
    /// <summary>
    /// Helper class to build test scenarios for WorkflowRunner with real (non-mocked) dependencies
    /// </summary>
    public class WorkflowTestBuilder
    {
        private readonly InMemoryWorkflowRegistry _registry;
        private readonly InMemoryWorkflowRunnerClient _client;
        private readonly InMemoryCommandHandlerFactory _handlerFactory;
        private readonly TestServiceProvider _serviceProvider;
        private readonly TestObjectSerializer _objectSerializer;

        public WorkflowTestBuilder()
        {
            _registry = new InMemoryWorkflowRegistry();
            _client = new InMemoryWorkflowRunnerClient();
            _handlerFactory = new InMemoryCommandHandlerFactory();
            _serviceProvider = new TestServiceProvider();
            _objectSerializer = new TestObjectSerializer();
        }

        public WorkflowTestBuilder RegisterWorkflow<TWorkflow>(string workflowType) where TWorkflow : WorkflowContainer
        {
            _registry.Workflows[workflowType] = (typeof(TWorkflow), typeof(TWorkflow));
            return this;
        }

        public WorkflowTestBuilder RegisterSignal<TSignal>(string signalIdentifier)
        {
            _registry.SignalTypes[signalIdentifier] = typeof(TSignal);
            return this;
        }

        public WorkflowTestBuilder SetupCommandHandler<TCommand, TResult>(
            string handlerKey,
            Func<TCommand, Task<TResult>> handler)
        {
            _handlerFactory.RegisterHandler(handlerKey, handler);
            _registry.CommandTypes[handlerKey] = (typeof(TCommand), typeof(TResult));
            return this;
        }

        public IWorkflowRunner Build()
        {
            var expressionSerializer = new TestExpressionSerializer();
            var delegateSerializer = new TestDelegateSerializer();
            var closureResolver = new TestClosureContextResolver();

            var mapper = new Mapper(
                expressionSerializer,
                _objectSerializer,
                delegateSerializer,
                closureResolver);

            var advancer = new StateMachineAdvancer();
            var templateCache = new WorkflowTemplateCache();

            return new WorkflowRunner(
                mapper,
                _registry,
                advancer,
                _client,
                _handlerFactory,
                _serviceProvider,
                templateCache,
                _objectSerializer);
        }

        public WorkflowExecutionRequest CreateExecutionRequest<TWorkflow>(
            Guid triggeringWaitId,
            string workflowType,
            WorkflowStateObject? stateObject = null,
            SignalDto? signal = null,
            List<Workflows.Abstraction.DTOs.Waits.WaitInfrastructureDto>? waits = null)
            where TWorkflow : WorkflowContainer
        {
            return new WorkflowExecutionRequest
            {
                TriggeringWaitId = triggeringWaitId,
                Signal = signal,
                WorkflowState = new WorkflowStateDto
                {
                    WorkflowType = workflowType,
                    StateObject = stateObject ?? new WorkflowStateObject
                    {
                        StateIndex = -1,
                        Instance = Activator.CreateInstance<TWorkflow>(),
                        StateMachinesObjects = new Dictionary<Guid, object>(),
                        WaitStatesObjects = new Dictionary<Guid, object>()
                    },
                    Waits = waits ?? new List<Workflows.Abstraction.DTOs.Waits.WaitInfrastructureDto>(),
                    Status = WorkflowInstanceStatus.Running
                }
            };
        }

        public Workflows.Abstraction.DTOs.Waits.SignalWaitDto CreateSignalWaitDto(
            string signalIdentifier,
            string waitName,
            Guid? waitId = null)
        {
            return new Workflows.Abstraction.DTOs.Waits.SignalWaitDto
            {
                Id = waitId ?? Guid.NewGuid(),
                SignalIdentifier = signalIdentifier,
                WaitName = waitName,
                Status = WaitStatus.Waiting,
                WaitType = WaitType.SignalWait,
                ChildWaits = new List<Workflows.Abstraction.DTOs.Waits.WaitInfrastructureDto>()
            };
        }

        public SignalDto CreateSignal(string signalIdentifier, object data)
        {
            return new SignalDto
            {
                SignalIdentifier = signalIdentifier,
                Data = data
            };
        }
    }
}
