using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Workflows.Abstraction.Common;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Runner;
using Workflows.Handler;
using Workflows.Handler.BaseUse;
using Workflows.Runner.ExpressionTransformers;

namespace Workflows.Runner
{
    internal class WorkflowRunner : IWorkflowRunner
    {
        private readonly TypesCache _workflowTypeCache;

        private readonly MatchExpressionTransformer _matchExpressionTransformer;
        private readonly IExpressionSerializer _expressionSerializer;
        private readonly MatchExpressionCache _matchExpressionCache;
        private readonly IObjectSerializer _objectSerializer;
        private readonly RunWorkflowSettings _settings;
        private readonly IWorkflowRunnerClient _runResultSender;
        private readonly ILogger<WorkflowRunner> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ICommandHandlerFactory _commandHandlerFactory;

        public WorkflowRunner(
            TypesCache workflowTypeCache,
            MatchExpressionTransformer matchExpressionTransformer,
            IExpressionSerializer expressionSerializer,
            MatchExpressionCache matchExpressionCache,
            IObjectSerializer objectSerializer,
            RunWorkflowSettings settings,
            IWorkflowRunnerClient runResultSender,
            ILogger<WorkflowRunner> logger,
            IServiceProvider serviceProvider,
            ICommandHandlerFactory commandHandlerFactory)
        {
            _workflowTypeCache = workflowTypeCache ?? throw new ArgumentNullException(nameof(workflowTypeCache));
            _matchExpressionTransformer = matchExpressionTransformer ?? throw new ArgumentNullException(nameof(matchExpressionTransformer));
            _expressionSerializer = expressionSerializer ?? throw new ArgumentNullException(nameof(expressionSerializer));
            _matchExpressionCache = matchExpressionCache ?? throw new ArgumentNullException(nameof(matchExpressionCache));
            _objectSerializer = objectSerializer ?? throw new ArgumentNullException(nameof(objectSerializer));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _runResultSender = runResultSender ?? throw new ArgumentNullException(nameof(runResultSender));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _commandHandlerFactory = commandHandlerFactory ?? throw new ArgumentNullException(nameof(commandHandlerFactory));
        }

        public async Task<WorkflowRunId> RunWorkflowAsync(WorkflowRunContext runContext)
        {
            if (runContext == null) throw new ArgumentNullException(nameof(runContext));
            if (runContext.WorkflowState == null) throw new ArgumentException("WorkflowState is required.", nameof(runContext));

            var runId = new WorkflowRunId
            {
                Id = Guid.NewGuid(),
                Name = $"WorkflowRun-{runContext.WorkflowState.Id}",
                Description = "Workflow runner execution"
            };

            var result = new WorkflowRunResult
            {
                Status = WorkflowInstanceStatus.InProgress,
                WorkflowState = runContext.WorkflowState,
                Message = "Workflow is in progress."
            };

            object workflowInstance = null;
            var shouldSerializeState = false;

            try
            {
                var workflowType = ResolveWorkflowType(runContext.WorkflowTypeName);
                workflowInstance = HydrateWorkflowInstance(workflowType, runContext.WorkflowState.StateObject);

                var incomingWait = SelectIncomingSignalWait(runContext.WorkflowState, runContext.Signal);
                if (incomingWait == null)
                {
                    result.Message = "No matching waiting signal was found in workflow state.";
                    return runId;
                }

                result.IncomingWait = incomingWait;

                if (IsWaitCancelledByToken(incomingWait, runContext.WorkflowState))
                {
                    incomingWait.Status = WaitStatus.Canceled;
                    incomingWait.PersistStatus = PersistStatus.Updated;
                    result.Message = "Wait was interrupted by a previously cancelled token.";
                    TryProceedExecution(runContext.WorkflowState, incomingWait);
                    shouldSerializeState = true;
                    return runId;
                }

                if (!EvaluateSignalMatch(incomingWait, runContext.Signal, workflowInstance))
                {
                    result.Message = "Signal did not satisfy the exact wait match expression.";
                    return runId;
                }

                incomingWait.Status = WaitStatus.Completed;
                incomingWait.PersistStatus = PersistStatus.Updated;

                if (!TryProceedExecution(runContext.WorkflowState, incomingWait))
                {
                    result.Message = "Signal matched, but workflow is still waiting for grouped or nested waits.";
                    shouldSerializeState = true;
                    return runId;
                }

                var nextWait = await AdvanceWorkflow(workflowInstance, incomingWait, runContext);

                if (nextWait == null)
                {
                    runContext.WorkflowState.Status = WorkflowInstanceStatus.Completed;
                    result.Status = WorkflowInstanceStatus.Completed;
                    result.Message = "Workflow completed successfully.";
                    result.IncomingWait = null;
                }
                else
                {
                    var nextWaitDto = nextWait.ToDto();
                    nextWaitDto.ParentWaitId = incomingWait.ParentWaitId;
                    nextWaitDto.RequestedByWorkflowId = incomingWait.RequestedByWorkflowId;
                    nextWaitDto.RootWorkflowId = incomingWait.RootWorkflowId;
                    nextWaitDto.WorkflowStateId = runContext.WorkflowState.Id;

                    PrepareWaitForPersistence(nextWait, nextWaitDto);
                    nextWaitDto.Status = WaitStatus.Waiting;
                    runContext.WorkflowState.Waits = new List<WaitInfrastructureDto> { nextWaitDto };
                    result.IncomingWait = nextWaitDto;
                }

                shouldSerializeState = true;
                return runId;
            }
            catch (Exception ex)
            {
                var actualException = ex is TargetInvocationException tie && tie.InnerException != null
                    ? tie.InnerException
                    : ex;

                _logger.LogError(actualException, "Error while running workflow instance {WorkflowStateId}.", runContext.WorkflowState.Id);
                runContext.WorkflowState.Status = WorkflowInstanceStatus.InError;

                if (result.IncomingWait != null)
                    result.IncomingWait.Status = _settings.WaitStatusIfProcessingError;

                result.Status = WorkflowInstanceStatus.InError;
                result.Message = actualException.ToString();
                result.WorkflowState = runContext.WorkflowState;

                return runId;
            }
            finally
            {
                if (shouldSerializeState && workflowInstance != null)
                {
                    runContext.WorkflowState.StateObject = _objectSerializer.Serialize(workflowInstance, SerializationScope.CompilerGeneratedClass);
                }

                result.WorkflowState = runContext.WorkflowState;
                await _runResultSender.SendWorkflowRunResultAsync(runId, result);
            }
        }

        private Type ResolveWorkflowType(string workflowTypeName)
        {
            if (string.IsNullOrWhiteSpace(workflowTypeName))
                throw new ArgumentException("WorkflowTypeName is required.", nameof(workflowTypeName));

            return _workflowTypeCache.GetOrAdd(workflowTypeName, ResolveWorkflowTypeCore);
        }

        private static Type ResolveWorkflowTypeCore(string workflowTypeName)
        {
            var workflowType = Type.GetType(workflowTypeName, throwOnError: false, ignoreCase: true);
            if (workflowType != null)
                return workflowType;

            workflowType = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(x => x.GetType(workflowTypeName, false, true))
                .FirstOrDefault(x => x != null);

            if (workflowType == null)
                throw new InvalidOperationException($"Unable to resolve workflow type [{workflowTypeName}].");

            return workflowType;
        }

        private object HydrateWorkflowInstance(Type workflowType, object stateObject)
        {
            if (stateObject == null)
                return ActivatorUtilities.CreateInstance(_serviceProvider, workflowType);

            if (stateObject is string serializedState)
            {
                //todo: why CompilerGeneratedClass use?
                return _objectSerializer.Deserialize(serializedState, workflowType);
            }

            if (workflowType.IsInstanceOfType(stateObject))
                return stateObject;

            var serialized = _objectSerializer.Serialize(stateObject);
            return _objectSerializer.Deserialize(serialized, workflowType);
        }

        private static SignalWaitDto SelectIncomingSignalWait(WorkflowStateDto workflowState, SignalDto signal)
        {
            if (workflowState?.Waits == null || signal == null) return null;

            return workflowState.Waits
                .Flatten(x => x.ChildWaits)
                .OfType<SignalWaitDto>()
                .FirstOrDefault(x =>
                    x.Status == WaitStatus.Waiting &&
                    string.Equals(x.SignalIdentifier, signal.SignalIdentifier, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryProceedExecution(WorkflowStateDto workflowState, WaitInfrastructureDto currentWait)
        {
            var parent = GetParentWait(workflowState, currentWait);
            while (parent != null)
            {
                if (parent is WaitsGroupDto || parent is SubWorkflowWaitDto)
                {
                    if (!IsWaitCompleted(parent))
                        return false;

                    parent.Status = WaitStatus.Completed;
                    parent.PersistStatus = PersistStatus.Updated;
                    CancelSubWaits(parent);
                }

                currentWait = parent;
                parent = GetParentWait(workflowState, currentWait);
            }

            return true;
        }

        private static WaitInfrastructureDto GetParentWait(WorkflowStateDto workflowState, WaitInfrastructureDto currentWait)
        {
            if (workflowState?.Waits == null || currentWait?.ParentWaitId == null)
                return null;

            var parentId = currentWait.ParentWaitId.Value;
            return workflowState.Waits
                .Flatten(x => x.ChildWaits)
                .FirstOrDefault(x => x.Id == parentId);
        }

        private static bool IsWaitCompleted(WaitInfrastructureDto wait)
        {
            if (wait == null)
                return false;

            if (wait is WaitsGroupDto group)
                return IsGroupWaitCompleted(group);

            if (wait is SubWorkflowWaitDto subWorkflow)
                return subWorkflow.ChildWaits?.Any(x => x.Status == WaitStatus.Waiting) is false;

            return wait.Status == WaitStatus.Completed;
        }

        private static bool IsGroupWaitCompleted(WaitsGroupDto group)
        {
            switch (group.WaitType)
            {
                case WaitType.GroupWaitAll:
                    return group.ChildWaits?.All(x => x.Status == WaitStatus.Completed) is true;

                case WaitType.GroupWaitFirst:
                    return group.ChildWaits?.Any(x => x.Status == WaitStatus.Completed) is true;

                case WaitType.GroupWaitWithExpression:
                    return group.ChildWaits?.Any(x => x.Status == WaitStatus.Waiting) is false;

                default:
                    return group.ChildWaits?.Any(x => x.Status == WaitStatus.Waiting) is false;
            }
        }

        private static void CancelSubWaits(WaitInfrastructureDto wait)
        {
            if (wait?.ChildWaits == null)
                return;

            foreach (var child in wait.ChildWaits.Flatten(x => x.ChildWaits).Where(x => x != wait && x.Status == WaitStatus.Waiting))
            {
                child.Status = WaitStatus.Canceled;
                child.PersistStatus = PersistStatus.Updated;
            }
        }

        private bool EvaluateSignalMatch(SignalWaitDto incomingWait, SignalDto signal, object workflowInstance)
        {
            if (incomingWait == null || signal == null) return false;
            if (incomingWait.IsExactMatchFullMatch) return true;

            var serializedExpression = !string.IsNullOrWhiteSpace(incomingWait.GenericMatchExpression)
                ? incomingWait.GenericMatchExpression
                : incomingWait.MatchExpression;

            if (string.IsNullOrWhiteSpace(serializedExpression))
                return true;

            try
            {
                var closure = ResolvePrivateDataValue(incomingWait.ClosureData);
                var evaluator = _matchExpressionCache.GetOrCompile(
                    serializedExpression,
                    () => _expressionSerializer.Deserialize(serializedExpression));

                return evaluator(signal.Data, closure ?? workflowInstance);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to evaluate match expression for wait {WaitId}.", incomingWait.Id);
                return false;
            }
        }

        private PrivateData SerializePrivateData(object value)
        {
            if (value == null)
                return null;

            return new PrivateData
            {
                Value = _objectSerializer.Serialize(value, SerializationScope.CompilerGeneratedClass),
                TypeName = value.GetType().AssemblyQualifiedName ?? value.GetType().FullName,
                Created = DateTime.UtcNow
            };
        }

        private async Task<Wait> AdvanceWorkflow(object workflowInstance, WaitInfrastructureDto incomingWait, WorkflowRunContext runContext = null)
        {
            if (workflowInstance is not WorkflowContainer container)
                throw new InvalidOperationException("Workflow instance must inherit from WorkflowContainer.");

            if (string.IsNullOrWhiteSpace(incomingWait.CallerName))
                return null;

            var method = container.GetType().GetMethod(
                incomingWait.CallerName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (method == null)
                throw new MissingMethodException(container.GetType().FullName, incomingWait.CallerName);

            var workflowStream = method.Invoke(container, null);
            if (workflowStream is not IAsyncEnumerable<Wait> asyncEnumerable)
                throw new InvalidOperationException($"Workflow method [{incomingWait.CallerName}] must return IAsyncEnumerable<Wait>.");

            var runner = ResolvePrivateDataValue(incomingWait.Locals) as IAsyncEnumerator<Wait> ?? asyncEnumerable.GetAsyncEnumerator();
            RestoreEnumeratorState(runner, container, incomingWait);

            var previousWait = incomingWait;

            while (await runner.MoveNextAsync())
            {
                var nextWait = runner.Current;

                if (nextWait is ICommandWait command)
                {
                    var handler = _commandHandlerFactory.GetHandler(command.HandlerKey);
                    await handler.ExecuteAsync(command, runContext);

                    if (command.ExecutionMode == CommandExecutionMode.Direct)
                    {
                        // Auto-advance: capture state so we can resume, but do not suspend
                        CaptureRunnerState(runner, previousWait, nextWait);
                        previousWait = nextWait.ToDto();
                        continue;
                    }

                    // Slow mode: suspend — treat this wait as the next suspension point
                    CaptureRunnerState(runner, previousWait, nextWait);
                    return nextWait;
                }

                CaptureRunnerState(runner, previousWait, nextWait);
                return nextWait;
            }

            return null;
        }

        private void RestoreEnumeratorState(IAsyncEnumerator<Wait> runner, WorkflowContainer workflowInstance, WaitInfrastructureDto incomingWait)
        {
            var runnerType = runner.GetType();

            var stateField = runnerType.GetField(Constants.CompilerStateFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            stateField?.SetValue(runner, incomingWait.StateAfterWait);

            try
            {
                var thisField = runnerType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(x => x.Name.EndsWith(Constants.CompilerCallerSuffix, StringComparison.Ordinal));

                if (thisField == null)
                {
                    _logger.LogWarning("Could not restore compiler-generated caller field ending with suffix {Suffix} on runner type {RunnerType}.", Constants.CompilerCallerSuffix, runnerType.FullName);
                }
                else
                {
                    thisField.SetValue(runner, workflowInstance);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed while restoring compiler-generated caller field for runner type {RunnerType}.", runnerType.FullName);
            }

            var closureValue = ResolvePrivateDataValue(incomingWait.ClosureData);
            if (closureValue != null)
            {
                var closureField = runnerType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(x => x.FieldType.Name.StartsWith(Constants.CompilerClosurePrefix, StringComparison.Ordinal));
                closureField?.SetValue(runner, closureValue);
            }
        }

        private object ResolvePrivateDataValue(PrivateData privateData)
        {
            if (privateData?.Value == null) return null;

            if (privateData.Value is string serialized && !string.IsNullOrWhiteSpace(privateData.TypeName))
            {
                var type = ResolveWorkflowType(privateData.TypeName);
                return _objectSerializer.Deserialize(serialized, type, SerializationScope.CompilerGeneratedClass);
            }

            return privateData.Value;
        }

        private void PrepareWaitForPersistence(Wait wait, WaitInfrastructureDto waitDto)
        {
            if (wait == null || waitDto == null)
                return;

            waitDto.PersistStatus = PersistStatus.New;

            if (wait is IPassiveWait passiveWait && passiveWait.CancelTokens?.Count > 0)
                waitDto.CancelTokens = passiveWait.CancelTokens;

            if (wait is ISignalWait signalWait && waitDto is SignalWaitDto signalWaitDto)
                PrepareSignalWaitForPersistence(signalWait, signalWaitDto);

            if (wait is GroupWait groupWait && waitDto is WaitsGroupDto waitsGroupDto)
                PrepareGroupWaitForPersistence(groupWait, waitsGroupDto);

            if (wait is SubWorkflowWait subWorkflowWait && waitDto is SubWorkflowWaitDto subWorkflowWaitDto)
                PrepareSubWorkflowWaitForPersistence(subWorkflowWait, subWorkflowWaitDto);

            if (waitDto.ChildWaits == null)
                return;

            foreach (var child in waitDto.ChildWaits)
            {
                child.ParentWaitId = waitDto.Id;
                child.PersistStatus = PersistStatus.New;
            }
        }

        private static bool IsWaitCancelledByToken(WaitInfrastructureDto wait, WorkflowStateDto workflowState)
        {
            if (wait?.CancelTokens == null || wait.CancelTokens.Count == 0)
                return false;

            if (workflowState?.CancelledTokens == null || workflowState.CancelledTokens.Count == 0)
                return false;

            return wait.CancelTokens.Any(t => workflowState.CancelledTokens.Contains(t));
        }

        private void PrepareGroupWaitForPersistence(GroupWait groupWait, WaitsGroupDto waitsGroupDto)
        {
            if (waitsGroupDto.ChildWaits == null || waitsGroupDto.ChildWaits.Count == 0)
                return;

            var runtimeChildren = groupWait.ChildWaitsRuntime;
            if (runtimeChildren == null || runtimeChildren.Count == 0)
                return;

            var count = Math.Min(runtimeChildren.Count, waitsGroupDto.ChildWaits.Count);
            for (var i = 0; i < count; i++)
            {
                var runtimeChild = runtimeChildren[i];
                var childDto = waitsGroupDto.ChildWaits[i];

                childDto.ParentWaitId = waitsGroupDto.Id;
                PrepareWaitForPersistence(runtimeChild, childDto);
            }
        }

        private void PrepareSignalWaitForPersistence(ISignalWait signalWait, SignalWaitDto signalWaitDto)
        {
            try
            {
                var transformed = _matchExpressionTransformer.Transform(signalWait);
                signalWaitDto.IsGenericMatchFullMatch = transformed.IsGenericMatchFullMatch;
                signalWaitDto.IsExactMatchFullMatch = transformed.IsExactMatchFullMatch;
                signalWaitDto.SignalExactMatchPaths = transformed.SignalExactMatchPaths;

                if (transformed.MatchExpression != null)
                    signalWaitDto.MatchExpression = _expressionSerializer.Serialize(transformed.MatchExpression);

                if (transformed.GenericMatchExpression is System.Linq.Expressions.LambdaExpression genericMatchLambda)
                    signalWaitDto.GenericMatchExpression = _expressionSerializer.Serialize(genericMatchLambda);

                if (transformed.InstanceExactMatchExpression != null)
                    signalWaitDto.ExactMatchPart = _expressionSerializer.Serialize(transformed.InstanceExactMatchExpression);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to transform and serialize expressions for wait {WaitName}.", signalWaitDto.WaitName);
            }
        }

        private void CaptureRunnerState(IAsyncEnumerator<Wait> runner, WaitInfrastructureDto previousWait, Wait nextWait)
        {
            if (nextWait?.ToDto() is not WaitInfrastructureDto nextWaitDto)
                return;

            var runnerType = runner.GetType();
            var stateField = runnerType.GetField(Constants.CompilerStateFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (stateField?.GetValue(runner) is int state)
                nextWaitDto.StateAfterWait = state;

            nextWaitDto.Locals = SerializePrivateData(runner);

            var closureField = runnerType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(x => x.FieldType.Name.StartsWith(Constants.CompilerClosurePrefix, StringComparison.Ordinal));
            var closureValue = closureField?.GetValue(runner);
            if (closureValue != null)
            {
                nextWaitDto.ClosureData = SerializePrivateData(closureValue);
            }
            else if (previousWait?.ClosureData != null)
            {
                nextWaitDto.ClosureData = previousWait.ClosureData;
            }
        }

        private void PrepareSubWorkflowWaitForPersistence(SubWorkflowWait subWorkflowWait, SubWorkflowWaitDto subWorkflowWaitDto)
        {
            if (subWorkflowWait.FirstWait == null)
                return;

            var firstWaitDto = subWorkflowWait.FirstWait;
            subWorkflowWaitDto.ChildWaits = new List<WaitInfrastructureDto> { firstWaitDto };

            PrepareWaitDtoTreeForPersistence(firstWaitDto, subWorkflowWaitDto.Id);
        }

        private void PrepareWaitDtoTreeForPersistence(WaitInfrastructureDto waitDto, Guid? parentWaitId)
        {
            if (waitDto == null)
                return;

            waitDto.ParentWaitId = parentWaitId;
            waitDto.PersistStatus = PersistStatus.New;

            if (waitDto.ChildWaits == null || waitDto.ChildWaits.Count == 0)
                return;

            foreach (var child in waitDto.ChildWaits)
                PrepareWaitDtoTreeForPersistence(child, waitDto.Id);
        }
    }
}
