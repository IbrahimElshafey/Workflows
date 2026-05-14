using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FastExpressionCompiler;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Runner.Cache;
using Workflows.Runner.DataObjects;

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
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
            _stateMachineAdvancer = stateMachineAdvancer ?? throw new ArgumentNullException(nameof(stateMachineAdvancer));
            _runResultSender = runResultSender ?? throw new ArgumentNullException(nameof(runResultSender));
            _commandHandlerFactory = commandHandlerFactory ?? throw new ArgumentNullException(nameof(commandHandlerFactory));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _templateCache = templateCache ?? throw new ArgumentNullException(nameof(templateCache));
        }

        public async Task<AsyncResult> RunWorkflowAsync(WorkflowExecutionRequest runContext)
        {
            if (runContext == null) throw new ArgumentNullException(nameof(runContext));
            if (runContext.WorkflowState == null) throw new ArgumentException("WorkflowState is required.", nameof(runContext));
            if (runContext.WorkflowState.Waits == null) throw new ArgumentException("WorkflowState.Waits is required.", nameof(runContext));

            var state = runContext.WorkflowState;
            var triggeringWaitDto = FindWaitById(state.Waits, runContext.TriggeringWaitId);

            if (triggeringWaitDto == null)
            {
                throw new InvalidOperationException($"Triggering wait with ID {runContext.TriggeringWaitId} not found.");
            }

            if (triggeringWaitDto.Status != WaitStatus.Waiting)
            {
                return Error("Triggering wait is not in Waiting status.");
            }

            if (!_workflowRegistry.Workflows.TryGetValue(state.WorkflowType, out var workflowTypes))
            {
                throw new InvalidOperationException($"Workflow {state.WorkflowType} not registered.");
            }

            var workflowFactory = _templateCache.GetOrAddWorkflowFactory(workflowTypes.WorkflowContainer);
            var workflowInstance = (WorkflowContainer)workflowFactory(_serviceProvider, null);

            // Map once, reuse.
            var triggeringWait = _mapper.MapToWait(triggeringWaitDto, _workflowRegistry);
            triggeringWait.WorkflowContainer = workflowInstance;

            if (triggeringWaitDto is SignalWaitDto signalWaitDto)
            {
                var signalValidationResult = ValidateAndApplySignal(signalWaitDto, triggeringWait as ISignalWait, runContext.Signal, workflowInstance);
                if (signalValidationResult != null)
                {
                    return signalValidationResult;
                }
            }

            var workflowInvoker = _templateCache.GetOrAddWorkflowInvoker(workflowTypes.WorkflowContainer, triggeringWait.CallerName);
            var workflowStream = (IAsyncEnumerable<Wait>)workflowInvoker(workflowInstance);

            var advancerResult = await _stateMachineAdvancer.RunAsync(workflowStream, state.StateObject).ConfigureAwait(false);

            state.StateObject = advancerResult?.State;

            var consumedWaitsIds = new List<Guid> { triggeringWaitDto.Id };
            var newWaits = new List<WaitInfrastructureDto>();

            if (advancerResult?.Wait != null)
            {
                newWaits.Add(_mapper.MapToDto(advancerResult.Wait));
            }
            else
            {
                state.Status = WorkflowInstanceStatus.Completed;
            }

            // NOTE: still single-branch semantics; group/tree merge logic should be added separately.
            state.Waits = newWaits;

            return new AsyncResult(
                Guid.NewGuid(),
                new
                {
                    NewWaitsIds = newWaits.Select(w => w.Id).ToList(),
                    ConsumedWaitsIds = consumedWaitsIds
                },
                "Accepted",
                "Workflow advanced.",
                DateTime.UtcNow);
        }

        private AsyncResult ValidateAndApplySignal(
            SignalWaitDto signalWaitDto,
            ISignalWait signalWait,
            SignalDto signal,
            WorkflowContainer workflowInstance)
        {
            if (signal == null)
            {
                return Error("Signal payload is required for SignalWait.");
            }

            if (!string.Equals(signalWaitDto.SignalIdentifier, signal.SignalIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return Error("Signal identifier mismatch.");
            }

            if (signalWait == null)
            {
                return Error("Triggering wait could not be mapped to signal wait.");
            }

            var compiledMatch = GetOrBuildCompiledMatch(signalWaitDto, signalWait);
            if (compiledMatch != null && !compiledMatch(signal.Data, workflowInstance, null))
            {
                return Error("Signal match expression failed.");
            }

            var afterMatchAction = signalWait.AfterMatchAction;
            if (afterMatchAction != null)
            {
                InvokeAfterMatchAction(afterMatchAction, signal.Data);
            }

            return null;
        }

        private Func<object, object, object, bool> GetOrBuildCompiledMatch(SignalWaitDto dto, ISignalWait wait)
        {
            if (dto.TemplateHashKey is string hashKey)
            {
                var cached = _templateCache.GetSignal(hashKey);
                if (cached?.CompiledMatchDelegate != null)
                {
                    return cached.CompiledMatchDelegate;
                }
            }

            if (wait.MatchExpression == null)
            {
                return null;
            }

            var signalType = _workflowRegistry.SignalTypes.TryGetValue(dto.SignalIdentifier, out var type)
                ? type
                : typeof(object);

            var compiled = CompileMatch(wait.MatchExpression, signalType);

            if (dto.TemplateHashKey is string cacheKey)
            {
                _templateCache.GetOrAddSignal(cacheKey, new SignalTemplateCacheRecord { CompiledMatchDelegate = compiled });
            }

            return compiled;
        }

        private void InvokeAfterMatchAction(object action, object signalData)
        {
            var invoker = _templateCache.GetOrAddAfterMatchInvoker(action.GetType());
            if (invoker == null)
            {
                throw new InvalidOperationException("AfterMatchAction signature is not supported or Invoke method not found.");
            }

            invoker(action, signalData);
        }

        private static WaitInfrastructureDto FindWaitById(IEnumerable<WaitInfrastructureDto> waits, Guid id)
        {
            if (waits == null) return null;

            foreach (var wait in waits)
            {
                if (wait == null) continue;
                if (wait.Id == id) return wait;

                var child = FindWaitById(wait.ChildWaits, id);
                if (child != null) return child;
            }

            return null;
        }

        private static Func<object, object, object, bool> CompileMatch(LambdaExpression matchExpression, Type signalType)
        {
            var signalParam = Expression.Parameter(typeof(object), "sig");
            var instanceParam = Expression.Parameter(typeof(object), "inst");
            var closureParam = Expression.Parameter(typeof(object), "clos");

            var args = new List<Expression>();
            var parameters = matchExpression.Parameters;

            if (parameters.Count >= 1)
                args.Add(Expression.Convert(signalParam, parameters[0].Type == typeof(object) ? signalType : parameters[0].Type));
            if (parameters.Count >= 2)
                args.Add(Expression.Convert(instanceParam, parameters[1].Type));
            if (parameters.Count >= 3)
                args.Add(Expression.Convert(closureParam, parameters[2].Type));

            var body = Expression.Invoke(matchExpression, args);
            var lambda = Expression.Lambda<Func<object, object, object, bool>>(body, signalParam, instanceParam, closureParam);
            return lambda.CompileFast();
        }

        private static AsyncResult Error(string message)
        {
            return new AsyncResult(Guid.NewGuid(), null, "Error", message, DateTime.UtcNow);
        }
    }
}
