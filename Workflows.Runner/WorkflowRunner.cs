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
        private readonly Abstraction.Runner.ICommandHandlerFactory _commandHandlerFactory;

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
            Abstraction.Runner.ICommandHandlerFactory commandHandlerFactory)
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

        public async Task<WorkflowRunId> RunWorkflowAsync(WorkflowExecutionRequest runContext)
        {
            if (runContext == null) throw new ArgumentNullException(nameof(runContext));
            if (runContext.WorkflowState == null) throw new ArgumentException("WorkflowState is required.", nameof(runContext));

            var runId = new WorkflowRunId
            {
                Id = Guid.NewGuid(),
                Name = $"WorkflowRun-{runContext.WorkflowState.Id}",
                Description = "Workflow runner execution"
            };

            var result = new WorkflowExecutionResponse
            {
                UpdatedState = runContext.WorkflowState,
                ExecutionCode = "Workflow is in progress."
            };

            object workflowInstance = null;
            var shouldSerializeState = false;

            try
            {
                var workflowType = ResolveWorkflowType(runContext.WorkflowState.WorkflowType);
                workflowInstance = HydrateWorkflowInstance(workflowType, runContext.WorkflowState.StateObject);

                var incomingWait = SelectIncomingSignalWait(runContext.WorkflowState, runContext.Signal);
                if (incomingWait == null)
                {
                    result.ExecutionCode = "No matching waiting signal was found in workflow state.";
                    return runId;
                }


                if (IsWaitCancelledByToken(incomingWait, runContext.WorkflowState))
                {
                    incomingWait.Status = Definition.Enums.WaitStatus.Canceled;
                    result.ExecutionCode = "Wait was interrupted by a previously cancelled token.";
                    TryProceedExecution(runContext.WorkflowState, incomingWait);
                    shouldSerializeState = true;
                    return runId;
                }

                if (!EvaluateSignalMatch(incomingWait, runContext.Signal, workflowInstance))
                {
                    result.ExecutionCode = "Signal did not satisfy the exact wait match expression.";
                    return runId;
                }

                incomingWait.Status = Definition.Enums.WaitStatus.Completed;

                if (!TryProceedExecution(runContext.WorkflowState, incomingWait))
                {
                    result.ExecutionCode = "Signal matched, but workflow is still waiting for grouped or nested waits.";
                    shouldSerializeState = true;
                    return runId;
                }

                var nextWait = await AdvanceWorkflow(workflowInstance, incomingWait, runContext);

                if (nextWait == null)
                {
                    runContext.WorkflowState.Status = WorkflowInstanceStatus.Completed;
                    result.UpdatedState.Status = WorkflowInstanceStatus.Completed;
                    result.ExecutionCode = "Workflow completed successfully.";
                }
                else
                {
                    var nextWaitDto = nextWait.ToDto();
                    nextWaitDto.ParentWaitId = incomingWait.ParentWaitId;
                    nextWaitDto.RequestedByWorkflowId = incomingWait.RequestedByWorkflowId;
                    nextWaitDto.RootWorkflowId = incomingWait.RootWorkflowId;
                    nextWaitDto.WorkflowStateId = runContext.WorkflowState.Id;

                    PrepareWaitForPersistence(nextWait, nextWaitDto);
                    nextWaitDto.Status = Definition.Enums.WaitStatus.Waiting;
                    runContext.WorkflowState.Waits = new List<Definition.DTOs.WaitInfrastructureDto> { nextWaitDto };
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

                //if (result.IncomingWait != null)
                //    result.IncomingWait.Status = _settings.WaitStatusIfProcessingError;

                result.ExecutionCode = actualException.ToString();
                result.UpdatedState = runContext.WorkflowState;

                return runId;
            }
            finally
            {
                if (shouldSerializeState && workflowInstance != null)
                {
                    runContext.WorkflowState.StateObject = _objectSerializer.Serialize(workflowInstance, SerializationScope.CompilerGeneratedClass);
                }

                result.UpdatedState = runContext.WorkflowState;
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

        private static Definition.DTOs.SignalWaitDto SelectIncomingSignalWait(WorkflowStateDto workflowState, SignalDto signal)
        {
            if (workflowState?.Waits == null || signal == null) return null;

            return workflowState.Waits
                .Flatten(x => x.ChildWaits)
                .OfType<Definition.DTOs.SignalWaitDto>()
                .FirstOrDefault(x =>
                    x.Status == Definition.Enums.WaitStatus.Waiting &&
                    string.Equals(x.SignalIdentifier, signal.SignalIdentifier, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryProceedExecution(WorkflowStateDto workflowState, Definition.DTOs.WaitInfrastructureDto currentWait)
        {
            var parent = GetParentWait(workflowState, currentWait);
            while (parent != null)
            {
                if (parent is Definition.DTOs.WaitsGroupDto || parent is Definition.DTOs.SubWorkflowWaitDto)
                {
                    if (!IsWaitCompleted(parent))
                        return false;

                    parent.Status = Definition.Enums.WaitStatus.Completed;
                    CancelSubWaits(parent);
                }

                currentWait = parent;
                parent = GetParentWait(workflowState, currentWait);
            }

            return true;
        }

        private static Definition.DTOs.WaitInfrastructureDto GetParentWait(WorkflowStateDto workflowState, Definition.DTOs.WaitInfrastructureDto currentWait)
        {
            if (workflowState?.Waits == null || currentWait?.ParentWaitId == null)
                return null;

            var parentId = currentWait.ParentWaitId.Value;
            return workflowState.Waits
                .Flatten(x => x.ChildWaits)
                .FirstOrDefault(x => x.Id == parentId);
        }

        private static bool IsWaitCompleted(Definition.DTOs.WaitInfrastructureDto wait)
        {
            if (wait == null)
                return false;

            if (wait is Definition.DTOs.WaitsGroupDto group)
                return IsGroupWaitCompleted(group);

            if (wait is Definition.DTOs.SubWorkflowWaitDto subWorkflow)
                return subWorkflow.ChildWaits?.Any(x => x.Status == Definition.Enums.WaitStatus.Waiting) is false;

            return wait.Status == Definition.Enums.WaitStatus.Completed;
        }

        private static bool IsGroupWaitCompleted(Definition.DTOs.WaitsGroupDto group)
        {
            switch (group.WaitType)
            {
                case Definition.Enums.WaitType.GroupWaitAll:
                    return group.ChildWaits?.All(x => x.Status == Definition.Enums.WaitStatus.Completed) is true;

                case Definition.Enums.WaitType.GroupWaitFirst:
                    return group.ChildWaits?.Any(x => x.Status == Definition.Enums.WaitStatus.Completed) is true;

                case Definition.Enums.WaitType.GroupWaitWithExpression:
                    return group.ChildWaits?.Any(x => x.Status == Definition.Enums.WaitStatus.Waiting) is false;

                default:
                    return group.ChildWaits?.Any(x => x.Status == Definition.Enums.WaitStatus.Waiting) is false;
            }
        }

        private static void CancelSubWaits(Definition.DTOs.WaitInfrastructureDto wait)
        {
            if (wait?.ChildWaits == null)
                return;

            foreach (var child in wait.ChildWaits.Flatten(x => x.ChildWaits).Where(x => x != wait && x.Status == Definition.Enums.WaitStatus.Waiting))
            {
                child.Status = Definition.Enums.WaitStatus.Canceled;
            }
        }

        private bool EvaluateSignalMatch(Definition.DTOs.SignalWaitDto incomingWait, SignalDto signal, object workflowInstance)
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

        private Definition.DTOs.PrivateData SerializePrivateData(object value)
        {
            if (value == null)
                return null;

            return new Definition.DTOs.PrivateData
            {
                Value = _objectSerializer.Serialize(value, SerializationScope.CompilerGeneratedClass),
                TypeName = value.GetType().AssemblyQualifiedName ?? value.GetType().FullName,
                Created = DateTime.UtcNow
            };
        }

        private async Task<Definition.Wait> AdvanceWorkflow(object workflowInstance, Definition.DTOs.WaitInfrastructureDto incomingWait, WorkflowExecutionRequest runContext = null)
        {
            if (workflowInstance is not Definition.WorkflowContainer container)
                throw new InvalidOperationException("Workflow instance must inherit from WorkflowContainer.");

            if (string.IsNullOrWhiteSpace(incomingWait.CallerName))
                return null;

            var method = container.GetType().GetMethod(
                incomingWait.CallerName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (method == null)
                throw new MissingMethodException(container.GetType().FullName, incomingWait.CallerName);

            var workflowStream = method.Invoke(container, null);
            if (workflowStream is not IAsyncEnumerable<Definition.Wait> asyncEnumerable)
                throw new InvalidOperationException($"Workflow method [{incomingWait.CallerName}] must return IAsyncEnumerable<Wait>.");

            var runner = ResolvePrivateDataValue(incomingWait.Locals) as IAsyncEnumerator<Definition.Wait> ?? asyncEnumerable.GetAsyncEnumerator();
            RestoreEnumeratorState(runner, container, incomingWait);

            var previousWait = incomingWait;

            while (await runner.MoveNextAsync())
            {
                var nextWait = runner.Current;

                if (nextWait is Definition.ICommandWait command)
                {
                    var handler = _commandHandlerFactory.GetHandler(command.HandlerKey);
                    await handler.ExecuteAsync(command, runContext);

                    if (command.ExecutionMode == Definition.Enums.CommandExecutionMode.Direct)
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

        private void RestoreEnumeratorState(IAsyncEnumerator<Definition.Wait> runner, Definition.WorkflowContainer workflowInstance, Definition.DTOs.WaitInfrastructureDto incomingWait)
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

        private object ResolvePrivateDataValue(Definition.DTOs.PrivateData privateData)
        {
            if (privateData?.Value == null) return null;

            if (privateData.Value is string serialized && !string.IsNullOrWhiteSpace(privateData.TypeName))
            {
                var type = ResolveWorkflowType(privateData.TypeName);
                return _objectSerializer.Deserialize(serialized, type, SerializationScope.CompilerGeneratedClass);
            }

            return privateData.Value;
        }

        private void PrepareWaitForPersistence(Definition.Wait wait, Definition.DTOs.WaitInfrastructureDto waitDto)
        {
            if (wait == null || waitDto == null)
                return;

            if (wait is Definition.IPassiveWait passiveWait && passiveWait.CancelTokens?.Count > 0)
                waitDto.CancelTokens = passiveWait.CancelTokens;

            if (wait is Definition.ISignalWait signalWait && waitDto is Definition.DTOs.SignalWaitDto signalWaitDto)
                PrepareSignalWaitForPersistence(signalWait, signalWaitDto);

            if (wait is Definition.GroupWait groupWait && waitDto is Definition.DTOs.WaitsGroupDto waitsGroupDto)
                PrepareGroupWaitForPersistence(groupWait, waitsGroupDto);

            if (wait is Definition.SubWorkflowWait subWorkflowWait && waitDto is Definition.DTOs.SubWorkflowWaitDto subWorkflowWaitDto)
                PrepareSubWorkflowWaitForPersistence(subWorkflowWait, subWorkflowWaitDto);

            if (waitDto.ChildWaits == null)
                return;

            foreach (var child in waitDto.ChildWaits)
            {
                child.ParentWaitId = waitDto.Id;
            }
        }

        private static bool IsWaitCancelledByToken(Definition.DTOs.WaitInfrastructureDto wait, WorkflowStateDto workflowState)
        {
            if (wait?.CancelTokens == null || wait.CancelTokens.Count == 0)
                return false;

            if (workflowState?.CancelledTokens == null || workflowState.CancelledTokens.Count == 0)
                return false;

            return wait.CancelTokens.Any(t => workflowState.CancelledTokens.Contains(t));
        }

        private void PrepareGroupWaitForPersistence(Definition.GroupWait groupWait, Definition.DTOs.WaitsGroupDto waitsGroupDto)
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

        private void PrepareSignalWaitForPersistence(Definition.ISignalWait signalWait, Definition.DTOs.SignalWaitDto signalWaitDto)
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

        private void CaptureRunnerState(IAsyncEnumerator<Definition.Wait> runner, Definition.DTOs.WaitInfrastructureDto previousWait, Definition.Wait nextWait)
        {
            if (nextWait?.ToDto() is not Definition.DTOs.WaitInfrastructureDto nextWaitDto)
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

        private void PrepareSubWorkflowWaitForPersistence(Definition.SubWorkflowWait subWorkflowWait, Definition.DTOs.SubWorkflowWaitDto subWorkflowWaitDto)
        {
            if (subWorkflowWait.FirstWait == null)
                return;

            var firstWaitDto = subWorkflowWait.FirstWait;
            subWorkflowWaitDto.ChildWaits = new List<Definition.DTOs.WaitInfrastructureDto> { firstWaitDto };

            PrepareWaitDtoTreeForPersistence(firstWaitDto, subWorkflowWaitDto.Id);
        }

        private void PrepareWaitDtoTreeForPersistence(Definition.DTOs.WaitInfrastructureDto waitDto, Guid? parentWaitId)
        {
            if (waitDto == null)
                return;

            waitDto.ParentWaitId = parentWaitId;

            if (waitDto.ChildWaits == null || waitDto.ChildWaits.Count == 0)
                return;

            foreach (var child in waitDto.ChildWaits)
                PrepareWaitDtoTreeForPersistence(child, waitDto.Id);
        }
    }
}
