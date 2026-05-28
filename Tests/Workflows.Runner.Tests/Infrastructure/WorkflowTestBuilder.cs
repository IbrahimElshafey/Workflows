using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.Helpers;

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

        public InMemoryWorkflowRunnerClient Client => _client;

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
            var services = new ServiceCollection();
            services.AddWorkflowsRunner();
            services.AddSingleton<IWorkflowRegistry>(_registry);
            services.AddSingleton<IWorkflowRunnerClient>(_client);
            services.AddSingleton<ICommandHandlerFactory>(_handlerFactory);
            services.AddSingleton<IObjectSerializer>(_objectSerializer);
            services.AddSingleton<IExpressionSerializer>(new TestExpressionSerializer());
            var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IWorkflowRunner>();
        }

        public WorkflowExecutionRequest CreateExecutionRequest<TWorkflow>(
            Guid triggeringWaitId,
            string workflowType,
            WorkflowStateObject? stateObject = null,
            SignalDto? signal = null,
            List<Workflows.Abstraction.DTOs.Waits.WaitInfrastructureDto>? waits = null)
            where TWorkflow : WorkflowContainer
        {
            if (stateObject != null)
            {
                stateObject.WorkflowType ??= workflowType;
            }
            return new WorkflowExecutionRequest
            {
                TriggeringWaitId = triggeringWaitId,
                Signal = signal,
                WorkflowState = new WorkflowStateDto
                {
                    WorkflowType = workflowType,
                    StateObject = stateObject ?? new WorkflowStateObject
                    {
                        WorkflowType = workflowType,
                        StateIndex = -1,
                        Instance = Activator.CreateInstance<TWorkflow>(),
                        StateMachinesObjects = new Dictionary<string, object>(),
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
