using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Runner.Cache;
using Workflows.Runner.DataObjects;
using Workflows.Shared.Serialization;

namespace Workflows.Runner
{
    internal class WorkflowRunner : IWorkflowRunner
    {
        private readonly IWorkflowRegistry _workflowRegistry;
        private readonly StateMachineAdvancer _stateMachineAdvancer;
        private readonly IWorkflowRunnerClient _runResultSender;
        private readonly ICommandHandlerFactory _commandHandlerFactory;
        private readonly IServiceProvider _serviceProvider;
        private readonly Mapper _mapper;
        private readonly WorkflowTemplateCache _templateCache;
        private readonly IObjectSerializer _objectSerializer;

        public WorkflowRunner(
            Mapper mapper,
            IWorkflowRegistry workflowRegistry,
            StateMachineAdvancer stateMachineAdvancer,
            IWorkflowRunnerClient runResultSender,
            ICommandHandlerFactory commandHandlerFactory,
            IServiceProvider serviceProvider,
            WorkflowTemplateCache templateCache,
            IObjectSerializer objectSerializer)
        {
            _mapper = mapper;
            _workflowRegistry = workflowRegistry;
            _stateMachineAdvancer = stateMachineAdvancer;
            _runResultSender = runResultSender;
            _commandHandlerFactory = commandHandlerFactory;
            _serviceProvider = serviceProvider;
            _templateCache = templateCache;
            _objectSerializer = objectSerializer;
        }

        public async Task<AsyncResult> RunWorkflowAsync(WorkflowExecutionRequest runContext)
        {
            var state = runContext.WorkflowState;
            var triggeringWaitDto = FindWaitById(state.Waits, runContext.TriggeringWaitId);

            if (triggeringWaitDto == null)
            {
                throw new InvalidOperationException($"Triggering wait with ID {runContext.TriggeringWaitId} not found.");
            }

            // 1. Resolve Workflow Types
            if (!_workflowRegistry.Workflows.TryGetValue(state.WorkflowType, out var workflowTypes))
            {
                throw new InvalidOperationException($"Workflow {state.WorkflowType} not registered.");
            }

            // 2. Instantiate/Hydrate Workflow Instance
            var workflowInstance = (WorkflowContainer)ActivatorUtilities.CreateInstance(_serviceProvider, workflowTypes.WorkflowContainer);

            // 3. Reconstruct Wait object from DTO to access its metadata
            var triggeringWait = _mapper.MapToWait(triggeringWaitDto, _workflowRegistry);
            triggeringWait.WorkflowContainer = workflowInstance;

            // 4. Handle Signal Match if applicable
            if (triggeringWait is ISignalWait signalWait && runContext.Signal != null)
            {
                if (signalWait.SignalIdentifier != runContext.Signal.SignalIdentifier)
                {
                    return new AsyncResult(Guid.NewGuid(), null, "Error", "Signal identifier mismatch.", DateTime.UtcNow);
                }

                if (signalWait.MatchExpression != null)
                {
                    Func<object, object, object, bool> compiledMatch = null;
                    if (triggeringWaitDto is SignalWaitDto signalWaitDto && signalWaitDto.TemplateHashKey is string hashKey)
                    {
                        var cacheRecord = _templateCache.GetSignal(hashKey);
                        if (cacheRecord != null)
                        {
                            compiledMatch = cacheRecord.CompiledMatchDelegate;
                        }
                        else
                        {
                            // Compile and cache (simplified for this task)
                            var compiled = signalWait.MatchExpression.Compile();
                            compiledMatch = (sig, inst, clos) => (bool)compiled.DynamicInvoke(sig);
                            _templateCache.GetOrAddSignal(hashKey, new SignalTemplateCacheRecord { CompiledMatchDelegate = compiledMatch });
                        }
                    }
                    else
                    {
                        var compiled = signalWait.MatchExpression.Compile();
                        compiledMatch = (sig, inst, clos) => (bool)compiled.DynamicInvoke(sig);
                    }

                    if (compiledMatch != null)
                    {
                        // Note: closure is not fully handled here as it requires more context from the DTO
                        if (!compiledMatch(runContext.Signal.Data, workflowInstance, null))
                        {
                            return new AsyncResult(Guid.NewGuid(), null, "Error", "Signal match expression failed.", DateTime.UtcNow);
                        }
                    }
                }

                var afterMatchAction = signalWait.AfterMatchAction;
                if (afterMatchAction != null)
                {
                    afterMatchAction.GetType().GetMethod("Invoke").Invoke(afterMatchAction, new[] { runContext.Signal.Data });
                }
            }

            // 5. Advance State Machine
            var workflowMethod = workflowTypes.WorkflowContainer.GetMethod(triggeringWait.CallerName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var workflowStream = (IAsyncEnumerable<Wait>)workflowMethod.Invoke(workflowInstance, null);

            var advancerResult = await _stateMachineAdvancer.RunAsync(workflowStream, state.StateObject);

            // 6. Process Results
            state.StateObject = advancerResult?.State;
            var consumedWaitsIds = new List<Guid> { triggeringWaitDto.Id };
            var newWaits = new List<WaitInfrastructureDto>();

            if (advancerResult?.Wait != null)
            {
                var nextWait = advancerResult.Wait;
                var nextWaitDto = _mapper.MapToDto(nextWait);
                newWaits.Add(nextWaitDto);
            }
            else
            {
                state.Status = Abstraction.Enums.WorkflowInstanceStatus.Completed;
            }

            state.Waits = newWaits;

            return new AsyncResult(Guid.NewGuid(),
                new { NewWaitsIds = newWaits.Select(w => w.Id).ToList(), ConsumedWaitsIds = consumedWaitsIds },
                "Accepted", "Workflow advanced.", DateTime.UtcNow);
        }

        private WaitInfrastructureDto FindWaitById(IEnumerable<WaitInfrastructureDto> waits, Guid id)
        {
            foreach (var wait in waits)
            {
                if (wait.Id == id) return wait;
                var child = FindWaitById(wait.ChildWaits, id);
                if (child != null) return child;
            }
            return null;
        }
    }
}
