using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Threading.Tasks;
using FastExpressionCompiler;
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

            // 1. Early Signal Validation
            if (triggeringWaitDto is SignalWaitDto signalDto && runContext.Signal != null)
            {
                if (signalDto.SignalIdentifier != runContext.Signal.SignalIdentifier)
                {
                    return new AsyncResult(Guid.NewGuid(), null, "Error", "Signal identifier mismatch.", DateTime.UtcNow);
                }
            }

            // 2. Resolve Workflow Types
            if (!_workflowRegistry.Workflows.TryGetValue(state.WorkflowType, out var workflowTypes))
            {
                throw new InvalidOperationException($"Workflow {state.WorkflowType} not registered.");
            }

            // 3. Instantiate/Hydrate Workflow Instance (Optimized)
            var factory = _templateCache.GetOrAddWorkflowFactory(workflowTypes.WorkflowContainer);
            var workflowInstance = (WorkflowContainer)factory(_serviceProvider, null);

            // 4. Handle Signal Match if applicable (Optimized)
            if (triggeringWaitDto is SignalWaitDto signalWaitDto && runContext.Signal != null)
            {
                Func<object, object, object, bool> compiledMatch = null;
                if (signalWaitDto.TemplateHashKey is string hashKey)
                {
                    var cacheRecord = _templateCache.GetSignal(hashKey);
                    if (cacheRecord != null)
                    {
                        compiledMatch = cacheRecord.CompiledMatchDelegate;
                    }
                }

                // If not cached, we MUST reconstruct to get the expression
                ISignalWait signalWait = null;
                if (compiledMatch == null)
                {
                    signalWait = (ISignalWait)_mapper.MapToWait(triggeringWaitDto, _workflowRegistry);
                    if (signalWait.MatchExpression != null)
                    {
                        var signalType = _workflowRegistry.SignalTypes.TryGetValue(signalWaitDto.SignalIdentifier, out var type) ? type : typeof(object);
                        compiledMatch = CompileMatch(signalWait.MatchExpression, signalType);
                        if (signalWaitDto.TemplateHashKey is string hKey)
                        {
                            _templateCache.GetOrAddSignal(hKey, new SignalTemplateCacheRecord { CompiledMatchDelegate = compiledMatch });
                        }
                    }
                }

                if (compiledMatch != null)
                {
                    if (!compiledMatch(runContext.Signal.Data, workflowInstance, null))
                    {
                        return new AsyncResult(Guid.NewGuid(), null, "Error", "Signal match expression failed.", DateTime.UtcNow);
                    }
                }

                // Execute AfterMatchAction
                signalWait ??= (ISignalWait)_mapper.MapToWait(triggeringWaitDto, _workflowRegistry);
                var afterMatchAction = signalWait.AfterMatchAction;
                if (afterMatchAction != null)
                {
                    ((dynamic)afterMatchAction).Invoke((dynamic)runContext.Signal.Data);
                }
            }

            // 5. Reconstruct Wait object from DTO for state machine advancement
            var triggeringWait = _mapper.MapToWait(triggeringWaitDto, _workflowRegistry);
            triggeringWait.WorkflowContainer = workflowInstance;

            // 6. Advance State Machine (Optimized)
            var workflowMethod = _templateCache.GetOrAddWorkflowMethod(workflowTypes.WorkflowContainer, triggeringWait.CallerName);
            var workflowStream = (IAsyncEnumerable<Wait>)workflowMethod.Invoke(workflowInstance, null);

            var advancerResult = await _stateMachineAdvancer.RunAsync(workflowStream, state.StateObject);

            // 7. Process Results
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

        private static Func<object, object, object, bool> CompileMatch(LambdaExpression matchExpression, Type signalType)
        {
            var signalParam = Expression.Parameter(typeof(object), "sig");
            var instParam = Expression.Parameter(typeof(object), "inst");
            var closureParam = Expression.Parameter(typeof(object), "clos");

            var convertedSignal = Expression.Convert(signalParam, signalType);
            var body = Expression.Invoke(matchExpression, convertedSignal);

            var lambda = Expression.Lambda<Func<object, object, object, bool>>(body, signalParam, instParam, closureParam);
            return lambda.CompileFast();
        }
    }
}
