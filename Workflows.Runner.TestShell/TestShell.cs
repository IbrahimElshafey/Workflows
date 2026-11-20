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
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Definition.Registration;
using Workflows.Runner.Pipeline;
using Workflows.Runner.Pipeline.CompletionChecker;
using Workflows.Runner;
using Workflows.Shared;

namespace Workflows.TestShell
{
    public class WorkflowTestShell : IDisposable
    {
        private ServiceProvider _serviceProvider;
        private TestShellCommandHandlerFactory _commandHandlerFactory;
        private readonly RehydrationMode _mode;
        private readonly List<Action<IWorkflowBuilder>> _registrations = new();
        private readonly List<Action<TestShellCommandHandlerFactory>> _handlerRegistrations = new();
        private readonly InMemoryTemplateRepository _templateRepository = new();
        private readonly List<ExecutionLogEntry> _executionLog = new();
        private string? _currentStateJson;
        private WorkflowStateDto? _currentState;
        private string _lastTriggeringWaitId = string.Empty;
        private bool _pendingRehydration;

        public IServiceProvider ServiceProvider => _serviceProvider;

        public WorkflowStateDto? CurrentState => _currentState;

        /// <summary>
        /// Rehydration mode the shell was created with.
        /// </summary>
        public RehydrationMode Mode => _mode;

        public IReadOnlyList<WaitInfrastructureDto> ActiveWaits => _currentState?.Waits?.Where(w => w.Status == WaitStatus.Waiting).ToList() ?? new List<WaitInfrastructureDto>();

        public WorkflowInstanceStatus CurrentStatus => _currentState?.Status ?? WorkflowInstanceStatus.New;

        public IReadOnlyList<ExecutionLogEntry> ExecutionLog => _executionLog;

        public WorkflowTestShell() : this(RehydrationMode.Direct)
        {
        }

        /// <summary>
        /// Creates a shell that keeps one runner alive for the whole workflow
        /// (<see cref="RehydrationMode.Direct"/>) or tears the provider - runner, workflow registry and
        /// command handlers - down after every yield and rebuilds it before resuming
        /// (<see cref="RehydrationMode.Rehydrating"/>).
        /// </summary>
        public WorkflowTestShell(RehydrationMode mode)
        {
            _mode = mode;
            (_serviceProvider, _commandHandlerFactory) = BuildProvider();
        }

        private (ServiceProvider Provider, TestShellCommandHandlerFactory CommandHandlerFactory) BuildProvider()
        {
            var services = new ServiceCollection();
            var commandHandlerFactory = new TestShellCommandHandlerFactory();

            // Register standard runner, serialization, and builder dependencies
            services.AddWorkflowsShared();
            services.AddWorkflowsRunner();

            // Override schemas and endpoints for pure in-memory test environment
            services.AddSingleton<JSchemaGenerator, MockSchemaGenerator>();
            services.AddSingleton<ICommandHandlerFactory>(commandHandlerFactory);
            services.AddSingleton<IWorkflowRunnerClient>(new TestShellWorkflowRunnerClient(OnWorkflowResultSent));
            services.AddSingleton<ITemplateRepository>(_templateRepository);

            var provider = services.BuildServiceProvider();

            var builder = provider.GetRequiredService<IWorkflowBuilder>();
            foreach (var register in _registrations)
            {
                register(builder);
            }

            foreach (var register in _handlerRegistrations)
            {
                register(commandHandlerFactory);
            }

            return (provider, commandHandlerFactory);
        }

        /// <summary>
        /// Discards the provider after a yield when running in <see cref="RehydrationMode.Rehydrating"/>
        /// so the next resume cannot observe any in-memory runner or registry state.
        /// </summary>
        private void RehydrateIfPending()
        {
            if (_mode != RehydrationMode.Rehydrating || !_pendingRehydration) return;

            _pendingRehydration = false;
            var stale = _serviceProvider;
            (_serviceProvider, _commandHandlerFactory) = BuildProvider();
            stale.Dispose();
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
            RestoreExternalChildren(response.UpdatedState.Waits, _currentState?.Waits);

            // Log step history for tracing runner internals
            _executionLog.Add(new ExecutionLogEntry
            {
                TriggeringWaitId = _lastTriggeringWaitId,
                ConsumedWaitIds = response.ConsumedWaitsIds?.Distinct().ToList() ?? new List<string>(),
                NewWaitIds = response.UpdatedState.Waits?.Where(w => w.Status == WaitStatus.Waiting).Select(w => w.Id).ToList() ?? new List<string>(),
                Status = response.UpdatedState.Status,
                SerializedStateSnapshot = _currentStateJson,
                Timestamp = DateTime.UtcNow
            });

            // Defer the teardown until the in-flight run has returned: the runner is still using
            // its scoped services while this callback executes.
            _pendingRehydration = true;
        }

        /// <summary>
        /// Fan-out children are runtime-only: the group DTO persists counts, and a real store keeps each
        /// child wait in its own row and rehydrates it on resume. The shell has no such store, so it copies
        /// the live children onto the deserialized snapshot - without them a fan-out could never resume here.
        /// </summary>
        /// <summary>
        /// Rebuilds the state handed to the runner. The persisted snapshot alone is not enough: a real
        /// store rehydrates fan-out child waits from their own rows, so the shell augments the round-tripped
        /// snapshot with the live children before resuming.
        /// </summary>
        private WorkflowStateDto? BuildResumeState()
        {
            var serializer = _serviceProvider.GetRequiredService<IObjectSerializer>();
            var stateForResume = serializer.Deserialize<WorkflowStateDto>(_currentStateJson!, SerializationScope.CompilerGeneratedClass);
            RestoreExternalChildren(_currentState?.Waits, stateForResume?.Waits);
            return stateForResume;
        }

        private static void RestoreExternalChildren(
            IEnumerable<WaitInfrastructureDto>? liveWaits,
            IEnumerable<WaitInfrastructureDto>? snapshotWaits)
        {
            if (liveWaits == null || snapshotWaits == null) return;

            var liveById = liveWaits.Where(w => w?.Id != null).ToDictionary(w => w.Id, w => w);
            foreach (var snapshotWait in snapshotWaits)
            {
                if (snapshotWait?.Id == null || !liveById.TryGetValue(snapshotWait.Id, out var liveWait)) continue;

                if (snapshotWait is ExternalGroupWaitDto snapshotGroup && liveWait is ExternalGroupWaitDto liveGroup)
                {
                    snapshotGroup.ExternalChildWaits = liveGroup.ExternalChildWaits;
                }

                RestoreExternalChildren(liveWait.ChildWaits, snapshotWait.ChildWaits);
            }
        }

        public WorkflowTestShell RegisterWorkflow<TWorkflow>() where TWorkflow : WorkflowContainer
        {
            _registrations.Add(builder => builder.RegisterWorkflow<TWorkflow>());
            _serviceProvider.GetRequiredService<IWorkflowBuilder>().RegisterWorkflow<TWorkflow>();
            return this;
        }

        public WorkflowTestShell RegisterWorkflow<TWorkflow>(string name, int version) where TWorkflow : WorkflowContainer
        {
            _registrations.Add(builder => builder.RegisterWorkflow<TWorkflow>(name, version));
            _serviceProvider.GetRequiredService<IWorkflowBuilder>().RegisterWorkflow<TWorkflow>(name, version);
            return this;
        }

        public WorkflowTestShell RegisterSignal<TSignal>(string signalIdentifier)
        {
            _registrations.Add(builder => builder.RegisterSignal<TSignal>(signalIdentifier));
            _serviceProvider.GetRequiredService<IWorkflowBuilder>().RegisterSignal<TSignal>(signalIdentifier);
            return this;
        }

        public WorkflowTestShell RegisterCommand<TCommand, TResult>(string commandIdentifier)
        {
            _registrations.Add(builder => builder.RegisterCommand<TCommand, TResult>(commandIdentifier));
            _serviceProvider.GetRequiredService<IWorkflowBuilder>().RegisterCommand<TCommand, TResult>(commandIdentifier);
            return this;
        }

        public WorkflowTestShell SetupCommandHandler<TCommand, TResult>(string handlerKey, Func<TCommand, Task<TResult>> handler)
        {
            _handlerRegistrations.Add(factory => factory.RegisterHandler(handlerKey, handler));
            _commandHandlerFactory.RegisterHandler(handlerKey, handler);
            return this;
        }

        public async Task<WorkflowStateDto?> StartWorkflowAsync(string workflowName, object? input = null)
        {
            _lastTriggeringWaitId = string.Empty;
            var runner = _serviceProvider.GetRequiredService<IWorkflowRunner>();
            await runner.StartWorkflow(workflowName, input);
            RehydrateIfPending();
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

            return await ResumeWithSignalAsync(activeWait, signalIdentifier, signalData);
        }

        /// <summary>
        /// Signals one specific wait by id. Required when several waits share a signal identifier,
        /// for example a fan-out group whose earlier children have already been consumed.
        /// </summary>
        public async Task<WorkflowStateDto?> SimulateSignalByWaitIdAsync(string waitId, object signalData)
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

            if (activeWait is not SignalWaitDto signalWait)
            {
                throw new InvalidOperationException($"Wait '{waitId}' is not a signal wait.");
            }

            return await ResumeWithSignalAsync(signalWait, signalWait.SignalIdentifier, signalData);
        }

        private async Task<WorkflowStateDto?> ResumeWithSignalAsync(WaitInfrastructureDto activeWait, string signalIdentifier, object signalData)
        {
            var stateForResume = BuildResumeState();

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
            RehydrateIfPending();
            return _currentState;
        }

        public async Task<WorkflowStateDto?> SimulateCommandResultByWaitIdAsync(string waitId, object commandResult)
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

            var stateForResume = BuildResumeState();

            _lastTriggeringWaitId = activeWait.Id;

            var request = new WorkflowExecutionRequest
            {
                TriggeringWaitId = activeWait.Id,
                CommandResult = commandResult,
                WorkflowState = stateForResume
            };

            var runner = _serviceProvider.GetRequiredService<IWorkflowRunner>();
            await runner.RunWorkflowAsync(request);
            RehydrateIfPending();
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

            return await SimulateCommandResultByWaitIdAsync(activeWait.Id, commandResult);
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

            var matcherFactory = scope.ServiceProvider.GetRequiredService<CompletionCheckerFactory>();
            var matcher = matcherFactory.GetChecker(waitDto);
            return await matcher.IsCompleted(waitDto);
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

            var matcherFactory = scope.ServiceProvider.GetRequiredService<CompletionCheckerFactory>();
            var matcher = matcherFactory.GetChecker(waitDto);
            return await matcher.IsCompleted(waitDto);
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

                // Fan-out groups keep their children outside ChildWaits.
                if (wait is ExternalGroupWaitDto externalGroup && externalGroup.ExternalChildWaits.Count > 0)
                {
                    var found = FindActiveWait(externalGroup.ExternalChildWaits, predicate);
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
