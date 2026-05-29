using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Schema.Generation;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Definition.Registration;
using Workflows.Runner.Pipeline;
using Workflows.Runner;
using Workflows.Runner.Pipeline.Matchers;
using Workflows.Shared;

namespace Workflows.TestShell
{
    public class WorkflowTestShell : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly TestShellCommandHandlerFactory _commandHandlerFactory = new();
        private readonly List<ExecutionLogEntry> _executionLog = new();
        private string? _currentStateJson;
        private WorkflowStateDto? _currentState;
        private Guid _lastTriggeringWaitId;

        public IServiceProvider ServiceProvider => _serviceProvider;

        public WorkflowStateDto? CurrentState => _currentState;

        public IReadOnlyList<WaitInfrastructureDto> ActiveWaits => _currentState?.Waits?.Where(w => w.Status == WaitStatus.Waiting).ToList() ?? new List<WaitInfrastructureDto>();

        public WorkflowInstanceStatus CurrentStatus => _currentState?.Status ?? WorkflowInstanceStatus.New;

        public IReadOnlyList<ExecutionLogEntry> ExecutionLog => _executionLog;

        public WorkflowTestShell()
        {
            var services = new ServiceCollection();

            // Register standard runner, serialization, and builder dependencies
            services.AddWorkflowsShared();
            services.AddWorkflowsRunner();

            // Override schemas and endpoints for pure in-memory test environment
            services.AddSingleton<JSchemaGenerator, MockSchemaGenerator>();
            services.AddSingleton<ICommandHandlerFactory>(_commandHandlerFactory);
            services.AddSingleton<IWorkflowRunnerClient>(new TestShellWorkflowRunnerClient(OnWorkflowResultSent));

            _serviceProvider = services.BuildServiceProvider();
        }

        private void OnWorkflowResultSent(AsyncResult runId, WorkflowExecutionResponse response)
        {
            if (response?.UpdatedState == null) return;

            var serializer = _serviceProvider.GetRequiredService<IObjectSerializer>();

            // 1. Serialize the state to JSON using CompilerGeneratedClass scope
            // This tests that custom classes, state machines, and waits can serialize properly
            var serialized = serializer.Serialize(response.UpdatedState, SerializationScope.CompilerGeneratedClass);
            if (serialized == null)
            {
                throw new InvalidOperationException("Failed to serialize workflow state.");
            }

            _currentStateJson = serialized.ToString();

            // 2. Deserialize it back to verify the roundtrip and update our cached DTO
            _currentState = serializer.Deserialize<WorkflowStateDto>(_currentStateJson, SerializationScope.CompilerGeneratedClass);

            // Log step history for tracing runner internals
            _executionLog.Add(new ExecutionLogEntry
            {
                TriggeringWaitId = _lastTriggeringWaitId,
                ConsumedWaitIds = response.ConsumedWaitsIds?.Distinct().ToList() ?? new List<Guid>(),
                NewWaitIds = response.UpdatedState.Waits?.Where(w => w.Status == WaitStatus.Waiting).Select(w => w.Id).ToList() ?? new List<Guid>(),
                Status = response.UpdatedState.Status,
                SerializedStateSnapshot = _currentStateJson,
                Timestamp = DateTime.UtcNow
            });
        }

        public WorkflowTestShell RegisterWorkflow<TWorkflow>() where TWorkflow : WorkflowContainer
        {
            var builder = _serviceProvider.GetRequiredService<IWorkflowBuilder>();
            builder.RegisterWorkflow<TWorkflow>();
            return this;
        }

        public WorkflowTestShell RegisterWorkflow<TWorkflow>(string name, int version) where TWorkflow : WorkflowContainer
        {
            var builder = _serviceProvider.GetRequiredService<IWorkflowBuilder>();
            builder.RegisterWorkflow<TWorkflow>(name, version);
            return this;
        }

        public WorkflowTestShell RegisterSignal<TSignal>(string signalIdentifier)
        {
            var builder = _serviceProvider.GetRequiredService<IWorkflowBuilder>();
            builder.RegisterSignal<TSignal>(signalIdentifier);
            return this;
        }

        public WorkflowTestShell RegisterCommand<TCommand, TResult>(string commandIdentifier)
        {
            var builder = _serviceProvider.GetRequiredService<IWorkflowBuilder>();
            builder.RegisterCommand<TCommand, TResult>(commandIdentifier);
            return this;
        }

        public WorkflowTestShell SetupCommandHandler<TCommand, TResult>(string handlerKey, Func<TCommand, Task<TResult>> handler)
        {
            _commandHandlerFactory.RegisterHandler(handlerKey, handler);
            return this;
        }

        public async Task<WorkflowStateDto?> StartWorkflowAsync(string workflowName, object? input = null)
        {
            _lastTriggeringWaitId = Guid.Empty;
            var runner = _serviceProvider.GetRequiredService<IWorkflowRunner>();
            await runner.StartWorkflow(workflowName, input);
            return _currentState;
        }

        public async Task<WorkflowStateDto?> SimulateSignalAsync(string signalIdentifier, object signalData)
        {
            if (_currentState == null || _currentStateJson == null)
            {
                throw new InvalidOperationException("No active workflow state exists. Start a workflow first.");
            }

            var activeWait = FindActiveWait(ActiveWaits, w => w is SignalWaitDto sw && sw.SignalIdentifier == signalIdentifier);
            if (activeWait == null)
            {
                throw new InvalidOperationException($"No active SignalWait found for signal identifier '{signalIdentifier}'.");
            }

            var serializer = _serviceProvider.GetRequiredService<IObjectSerializer>();
            var stateForResume = serializer.Deserialize<WorkflowStateDto>(_currentStateJson, SerializationScope.CompilerGeneratedClass);

            _lastTriggeringWaitId = activeWait.Id;

            var request = new WorkflowExecutionRequest
            {
                TriggeringWaitId = activeWait.Id,
                Signal = new SignalDto
                {
                    SignalIdentifier = signalIdentifier,
                    Data = signalData
                },
                WorkflowState = stateForResume
            };

            var runner = _serviceProvider.GetRequiredService<IWorkflowRunner>();
            await runner.RunWorkflowAsync(request);
            return _currentState;
        }

        public async Task<WorkflowStateDto?> SimulateCommandResultAsync(Guid waitId, object commandResult)
        {
            if (_currentState == null || _currentStateJson == null)
            {
                throw new InvalidOperationException("No active workflow state exists. Start a workflow first.");
            }

            var activeWait = FindActiveWait(ActiveWaits, w => w.Id == waitId);
            if (activeWait == null)
            {
                throw new InvalidOperationException($"No active wait found with ID '{waitId}'.");
            }

            var serializer = _serviceProvider.GetRequiredService<IObjectSerializer>();
            var stateForResume = serializer.Deserialize<WorkflowStateDto>(_currentStateJson, SerializationScope.CompilerGeneratedClass);

            _lastTriggeringWaitId = activeWait.Id;

            var request = new WorkflowExecutionRequest
            {
                TriggeringWaitId = activeWait.Id,
                CommandResult = commandResult,
                WorkflowState = stateForResume
            };

            var runner = _serviceProvider.GetRequiredService<IWorkflowRunner>();
            await runner.RunWorkflowAsync(request);
            return _currentState;
        }

        public async Task<WorkflowStateDto?> SimulateCommandResultAsync(string commandIdentifier, object commandResult)
        {
            if (_currentState == null || _currentStateJson == null)
            {
                throw new InvalidOperationException("No active workflow state exists. Start a workflow first.");
            }

            var activeWait = FindActiveWait(ActiveWaits, w => w is CommandWaitDto cw && (cw.HandlerKey == commandIdentifier || cw.WaitName == commandIdentifier));
            if (activeWait == null)
            {
                throw new InvalidOperationException($"No active CommandWait found matching command identifier '{commandIdentifier}'.");
            }

            return await SimulateCommandResultAsync(activeWait.Id, commandResult);
        }

        /// <summary>
        /// Isolated Matcher evaluation tool for runner authors to test internal matchers.
        /// </summary>
        public async Task<bool> SimulateMatchAsync(WaitInfrastructureDto waitDto, SignalDto signal)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WorkflowExecutionContext>();
            context.Signal = signal;
            context.TriggeringWaitId = waitDto.Id;
            context.WorkflowState = _currentState ?? new WorkflowStateDto 
            { 
                WorkflowType = _currentState?.WorkflowType,
                StateObject = new WorkflowStateObject { WorkflowType = _currentState?.WorkflowType } 
            };
            context.WorkflowInstance = _currentState?.StateObject?.Instance as WorkflowContainer;

            var matcherFactory = scope.ServiceProvider.GetRequiredService<MatcherFactory>();
            var matcher = matcherFactory.GetMatcher(waitDto);
            return await matcher.MatchAsync(waitDto);
        }

        /// <summary>
        /// Isolated Matcher evaluation tool for runner authors to test internal matchers.
        /// </summary>
        public async Task<bool> SimulateMatchAsync(WaitInfrastructureDto waitDto, object commandResult)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WorkflowExecutionContext>();
            context.CommandResult = commandResult;
            context.TriggeringWaitId = waitDto.Id;
            context.WorkflowState = _currentState ?? new WorkflowStateDto 
            { 
                WorkflowType = _currentState?.WorkflowType,
                StateObject = new WorkflowStateObject { WorkflowType = _currentState?.WorkflowType } 
            };
            context.WorkflowInstance = _currentState?.StateObject?.Instance as WorkflowContainer;

            var matcherFactory = scope.ServiceProvider.GetRequiredService<MatcherFactory>();
            var matcher = matcherFactory.GetMatcher(waitDto);
            return await matcher.MatchAsync(waitDto);
        }

        private WaitInfrastructureDto? FindActiveWait(IEnumerable<WaitInfrastructureDto> waits, Func<WaitInfrastructureDto, bool> predicate)
        {
            if (waits == null) return null;
            foreach (var wait in waits)
            {
                if (predicate(wait)) return wait;
                if (wait.ChildWaits != null && wait.ChildWaits.Count > 0)
                {
                    var found = FindActiveWait(wait.ChildWaits, predicate);
                    if (found != null) return found;
                }
            }
            return null;
        }

        public void Dispose()
        {
            _serviceProvider?.Dispose();
        }
    }
}
