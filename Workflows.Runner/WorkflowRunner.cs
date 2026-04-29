using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private readonly MatchExpressionTransformer _matchExpressionTransformer;
        private readonly Abstraction.Runner.IExpressionSerializer _expressionSerializer;
        private readonly MatchExpressionCache _matchExpressionCache;
        private readonly IObjectSerializer _objectSerializer;
        private readonly RunWorkflowSettings _settings;
        private readonly IWorkflowRunResultSender _runResultSender;
        private readonly ILogger<WorkflowRunner> _logger;

        public WorkflowRunner(
            MatchExpressionTransformer matchExpressionTransformer,
            Abstraction.Runner.IExpressionSerializer expressionSerializer,
            MatchExpressionCache matchExpressionCache,
            IObjectSerializer objectSerializer,
            RunWorkflowSettings settings,
            IWorkflowRunResultSender runResultSender,
            ILogger<WorkflowRunner> logger)
        {
            _matchExpressionTransformer = matchExpressionTransformer ?? throw new ArgumentNullException(nameof(matchExpressionTransformer));
            _expressionSerializer = expressionSerializer ?? throw new ArgumentNullException(nameof(expressionSerializer));
            _matchExpressionCache = matchExpressionCache ?? throw new ArgumentNullException(nameof(matchExpressionCache));
            _objectSerializer = objectSerializer ?? throw new ArgumentNullException(nameof(objectSerializer));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _runResultSender = runResultSender ?? throw new ArgumentNullException(nameof(runResultSender));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public WorkflowRunId RunWorkflow(WorkflowRunContext runContext)
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

            try
            {
                var workflowType = ResolveWorkflowType(runContext.WorkflowTypeName);
                var workflowInstance = HydrateWorkflowInstance(workflowType, runContext.WorkflowState.StateObject);

                var incomingWait = SelectIncomingSignalWait(runContext.WorkflowState, runContext.Signal);
                if (incomingWait == null)
                {
                    result.Message = "No matching waiting signal was found in workflow state.";
                    _runResultSender.SendWorkflowRunResult(runId, result).GetAwaiter().GetResult();
                    return runId;
                }

                result.IncomingWait = incomingWait;

                if (!EvaluateSignalMatch(incomingWait, runContext.Signal, workflowInstance))
                {
                    result.Message = "Signal did not satisfy the exact wait match expression.";
                    _runResultSender.SendWorkflowRunResult(runId, result).GetAwaiter().GetResult();
                    return runId;
                }

                incomingWait.Status = WaitStatus.Completed;

                var nextWait = AdvanceWorkflow(workflowInstance, incomingWait);
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
                    PrepareWaitForPersistence(nextWait, nextWaitDto);
                    nextWaitDto.Status = WaitStatus.Waiting;
                    runContext.WorkflowState.Waits = new List<WaitBaseDto> { nextWaitDto };
                    result.IncomingWait = nextWaitDto;
                }

                runContext.WorkflowState.StateObject = _objectSerializer.Serialize(workflowInstance, SerializationScope.InternalState);
                result.WorkflowState = runContext.WorkflowState;

                _runResultSender.SendWorkflowRunResult(runId, result).GetAwaiter().GetResult();
                return runId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while running workflow instance {WorkflowStateId}.", runContext.WorkflowState.Id);
                runContext.WorkflowState.Status = WorkflowInstanceStatus.InError;

                if (result.IncomingWait != null)
                    result.IncomingWait.Status = _settings.WaitStatusIfProcessingError;

                result.Status = WorkflowInstanceStatus.InError;
                result.Message = ex.ToString();
                result.WorkflowState = runContext.WorkflowState;

                _runResultSender.SendWorkflowRunResult(runId, result).GetAwaiter().GetResult();
                return runId;
            }
        }

        private static Type ResolveWorkflowType(string workflowTypeName)
        {
            if (string.IsNullOrWhiteSpace(workflowTypeName))
                throw new ArgumentException("WorkflowTypeName is required.", nameof(workflowTypeName));

            var workflowType = Type.GetType(workflowTypeName);
            if (workflowType != null) return workflowType;

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
                return Activator.CreateInstance(workflowType);

            if (stateObject is string serializedState)
            {
                return _objectSerializer.Deserialize(serializedState, workflowType, SerializationScope.InternalState);
            }

            if (workflowType.IsInstanceOfType(stateObject))
                return stateObject;

            var serialized = _objectSerializer.Serialize(stateObject, SerializationScope.InternalState);
            return _objectSerializer.Deserialize(serialized, workflowType, SerializationScope.InternalState);
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
                var closure = ResolvePrivateDataValue(incomingWait.Locals);
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

        private Wait AdvanceWorkflow(object workflowInstance, SignalWaitDto incomingWait)
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

            var runner = asyncEnumerable.GetAsyncEnumerator();
            RestoreEnumeratorState(runner, container, incomingWait);

            var hasNext = runner.MoveNextAsync().AsTask().GetAwaiter().GetResult();
            return hasNext ? runner.Current : null;
        }

        private void RestoreEnumeratorState(IAsyncEnumerator<Wait> runner, WorkflowContainer workflowInstance, SignalWaitDto incomingWait)
        {
            var runnerType = runner.GetType();

            var stateField = runnerType.GetField(Constants.CompilerStateFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            stateField?.SetValue(runner, incomingWait.StateAfterWait);

            var thisField = runnerType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(x => x.Name.EndsWith(Constants.CompilerCallerSuffix, StringComparison.Ordinal));
            thisField?.SetValue(runner, workflowInstance);

            var localsValue = ResolvePrivateDataValue(incomingWait.Locals);
            if (localsValue != null)
            {
                var closureField = runnerType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(x => x.FieldType.Name.StartsWith(Constants.CompilerClosurePrefix, StringComparison.Ordinal));
                closureField?.SetValue(runner, localsValue);
            }
        }

        private object ResolvePrivateDataValue(PrivateData privateData)
        {
            if (privateData?.Value == null) return null;

            if (privateData.Value is string serialized && !string.IsNullOrWhiteSpace(privateData.TypeName))
            {
                var type = ResolveWorkflowType(privateData.TypeName);
                return _objectSerializer.Deserialize(serialized, type, SerializationScope.InternalState);
            }

            return privateData.Value;
        }

        private void PrepareWaitForPersistence(Wait wait, WaitBaseDto waitDto)
        {
            if (wait is not ISignalWait signalWait || waitDto is not SignalWaitDto signalWaitDto)
                return;

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
                _logger.LogWarning(ex, "Failed to transform and serialize expressions for wait {WaitName}.", waitDto.WaitName);
            }
        }
    }
}
