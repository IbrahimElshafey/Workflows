using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Runner.Cache;

namespace Workflows.Runner.Tests.Infrastructure
{
    /// <summary>
    /// Helper class to build test scenarios for WorkflowRunner
    /// </summary>
    public class WorkflowTestBuilder
    {
        private readonly Mock<IWorkflowRegistry> _registryMock;
        private readonly Mock<IWorkflowRunnerClient> _clientMock;
        private readonly Mock<ICommandHandlerFactory> _handlerFactoryMock;
        private readonly Mock<IServiceProvider> _serviceProviderMock;
        private readonly Mock<IObjectSerializer> _serializerMock;

        private readonly Dictionary<string, WorkflowType> _registeredWorkflows = new();
        private readonly Dictionary<string, Type> _registeredSignals = new();

        public WorkflowTestBuilder()
        {
            _registryMock = new Mock<IWorkflowRegistry>();
            _clientMock = new Mock<IWorkflowRunnerClient>();
            _handlerFactoryMock = new Mock<ICommandHandlerFactory>();
            _serviceProviderMock = new Mock<IServiceProvider>();
            _serializerMock = new Mock<IObjectSerializer>();

            // Setup basic registry behavior
            _registryMock.Setup(r => r.Workflows).Returns(_registeredWorkflows);
            _registryMock.Setup(r => r.SignalTypes).Returns(_registeredSignals);
        }

        public WorkflowTestBuilder RegisterWorkflow<TWorkflow>(string workflowType) where TWorkflow : WorkflowContainer
        {
            _registeredWorkflows[workflowType] = new WorkflowType
            {
                WorkflowContainer = typeof(TWorkflow)
            };

            // Setup service provider to create workflow instances
            _serviceProviderMock.Setup(sp => sp.GetService(typeof(TWorkflow)))
                .Returns(() => Activator.CreateInstance<TWorkflow>());

            return this;
        }

        public WorkflowTestBuilder RegisterSignal<TSignal>(string signalIdentifier)
        {
            _registeredSignals[signalIdentifier] = typeof(TSignal);
            return this;
        }

        public WorkflowTestBuilder SetupCommandHandler<TCommand, TResult>(
            string handlerKey,
            Func<TCommand, Task<TResult>> handler)
        {
            var handlerMock = new Mock<ICommandHandler>();
            handlerMock.Setup(h => h.ExecuteAsync(It.IsAny<ICommandWait>(), It.IsAny<WorkflowExecutionRequest>()))
                .Returns<ICommandWait, WorkflowExecutionRequest>(async (cmd, ctx) =>
                {
                    // Extract command data and execute handler
                    await Task.CompletedTask;
                });

            _handlerFactoryMock.Setup(f => f.GetHandler(handlerKey))
                .Returns(handlerMock.Object);

            return this;
        }

        public IWorkflowRunner Build()
        {
            var mapper = new Mapper(_serializerMock.Object);
            var advancer = new StateMachineAdvancer();
            var templateCache = new WorkflowTemplateCache();

            return new WorkflowRunner(
                mapper,
                _registryMock.Object,
                advancer,
                _clientMock.Object,
                _handlerFactoryMock.Object,
                _serviceProviderMock.Object,
                templateCache,
                _serializerMock.Object);
        }

        public WorkflowExecutionRequest CreateExecutionRequest<TWorkflow>(
            Guid triggeringWaitId,
            string workflowType,
            StateMachineObject? stateObject = null,
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
                    StateObject = stateObject ?? new StateMachineObject
                    {
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
                WaitType = WaitType.Signal,
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
