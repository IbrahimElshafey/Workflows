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
using Workflows.Primitives;
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

            // Restore cancelled tokens to workflow instance
            if (state.CancelledTokens != null)
            {
                workflowInstance.TokensToCancel = new HashSet<string>(state.CancelledTokens);
            }

            // Map once, reuse. Pass StateMachineObject to restore ExplicitState
            var triggeringWait = _mapper.MapToWait(triggeringWaitDto, _workflowRegistry, state.StateObject);
            triggeringWait.WorkflowContainer = workflowInstance;

            if (triggeringWaitDto is SignalWaitDto signalWaitDto)
            {
                var signalValidationResult = ValidateAndApplySignal(signalWaitDto, triggeringWait as ISignalWait, runContext.Signal, workflowInstance);
                if (signalValidationResult != null)
                {
                    return signalValidationResult;
                }
            }
            else if (triggeringWaitDto is CommandWaitDto commandWaitDto)
            {
                var commandExecutionResult = await ExecuteCommandAsync(commandWaitDto, triggeringWait as Definition.ICommandWait, runContext, workflowInstance);
                if (commandExecutionResult != null)
                {
                    return commandExecutionResult;
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
                var nextWait = advancerResult.Wait;

                // Handle active wait types that execute immediately
                if (nextWait is CompensationWait compensationWait)
                {
                    // Execute compensation logic in-memory
                    await ExecuteCompensationAsync(compensationWait, state, workflowInstance);

                    // Loop back to get next wait after compensation
                    advancerResult = await _stateMachineAdvancer.RunAsync(workflowStream, state.StateObject).ConfigureAwait(false);
                    nextWait = advancerResult?.Wait;
                }

                // Check if next wait is cancelled before processing
                if (nextWait != null && IsWaitCancelled(nextWait, state.CancelledTokens))
                {
                    // Invoke OnCanceled callback if present
                    await InvokeCancelActionAsync(nextWait);

                    // Skip this wait and get next one
                    advancerResult = await _stateMachineAdvancer.RunAsync(workflowStream, state.StateObject).ConfigureAwait(false);
                    nextWait = advancerResult?.Wait;
                }

                if (nextWait != null)
                {
                    // Save ExplicitState to StateMachineObject.WaitStatesObjects (deduplicated)
                    SaveWaitStatesToMachineState(nextWait, state.StateObject);

                    newWaits.Add(_mapper.MapToDto(nextWait));
                }
                else
                {
                    state.Status = WorkflowInstanceStatus.Completed;
                }
            }
            else
            {
                state.Status = WorkflowInstanceStatus.Completed;
            }

            // Sync cancelled tokens from workflow instance back to state
            if (workflowInstance.TokensToCancel.Count > 0)
            {
                state.CancelledTokens = new HashSet<string>(workflowInstance.TokensToCancel);
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
            if (compiledMatch != null && !compiledMatch(signal.Data, workflowInstance, signalWait.ExplicitState))
            {
                return Error("Signal match expression failed.");
            }

            var afterMatchAction = signalWait.AfterMatchAction;
            if (afterMatchAction != null)
            {
                InvokeAfterMatchAction(afterMatchAction, signal.Data, signalWait.ExplicitState);
            }

            return null;
        }

        private async Task<AsyncResult> ExecuteCommandAsync(
            CommandWaitDto commandWaitDto,
            Definition.ICommandWait commandWait,
            WorkflowExecutionRequest runContext,
            WorkflowContainer workflowInstance)
        {
            if (commandWait == null)
            {
                return Error("Triggering wait could not be mapped to command wait.");
            }

            // Get command wait properties via reflection
            var commandWaitType = commandWait.GetType();
            var commandDataProperty = commandWaitType.GetProperty("CommandData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var onResultActionProperty = commandWaitType.GetProperty("OnResultAction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var onFailureActionProperty = commandWaitType.GetProperty("OnFailureAction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var compensationActionProperty = commandWaitType.GetProperty("CompensationAction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var tokensProperty = commandWaitType.GetProperty("Tokens", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var explicitStateProperty = commandWaitType.BaseType.GetProperty("ExplicitState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

            var commandData = commandDataProperty?.GetValue(commandWait);
            var onResultAction = onResultActionProperty?.GetValue(commandWait);
            var onFailureAction = onFailureActionProperty?.GetValue(commandWait);
            var compensationAction = compensationActionProperty?.GetValue(commandWait);
            var tokens = tokensProperty?.GetValue(commandWait) as List<string>;
            var explicitState = explicitStateProperty?.GetValue(commandWait);

            try
            {
                // TODO: Execute command through handler factory
                // For now, simulate execution based on execution mode
                object result = null;

                // Check if this is a Direct mode command (fast, synchronous)
                if (commandWaitDto.ExecutionMode == CommandExecutionMode.Direct)
                {
                    // Execute immediately via handler
                    // var handler = _commandHandlerFactory.GetHandler(commandWait.HandlerKey);
                    // result = await handler.ExecuteAsync(commandData);

                    // Placeholder: simulate successful execution
                    result = CreateMockResult(commandData);
                }
                else
                {
                    // Dispatched mode - command was already executed externally
                    // Result should be in the execution request
                    result = runContext.CommandResult;
                }

                // Track command execution for compensation
                TrackCommandExecution(
                    runContext.WorkflowState.StateObject,
                    commandData?.GetType().Name ?? "UnknownCommand",
                    result,
                    explicitState,
                    tokens,
                    compensationAction);

                // Invoke OnResult callback if present
                if (onResultAction != null)
                {
                    InvokeOnResultAction(onResultAction, result, explicitState);
                }

                return null; // Success, continue workflow
            }
            catch (Exception ex)
            {
                // Invoke OnFailure callback if present
                if (onFailureAction != null)
                {
                    await InvokeOnFailureActionAsync(onFailureAction, ex, explicitState);
                }

                return Error($"Command execution failed: {ex.Message}");
            }
        }

        private object CreateMockResult(object commandData)
        {
            // Create a mock result for testing purposes
            // In production, this would come from the actual handler
            var resultType = commandData?.GetType().Name.Replace("Command", "Result");
            return new { Success = true, ResultType = resultType };
        }

        private void TrackCommandExecution(
            StateMachineObject stateObject,
            string commandType,
            object result,
            object explicitState,
            List<string> tokens,
            object compensationAction)
        {
            var history = BuildCommandHistory(stateObject);

            history.Add(new CommandHistoryEntry
            {
                CommandType = commandType,
                Result = result,
                ExplicitState = explicitState,
                Tokens = tokens ?? new List<string>(),
                CompensationAction = compensationAction,
                IsCompensated = false,
                ExecutionOrder = history.Count
            });

            UpdateCommandHistoryInState(stateObject, history);
        }

        private void InvokeOnResultAction(object action, object result, object explicitState)
        {
            var invoker = _templateCache.GetOrAddOnResultInvoker(action.GetType());
            if (invoker == null)
            {
                throw new InvalidOperationException("OnResultAction signature is not supported or Invoke method not found.");
            }

            invoker(action, result, explicitState);
        }

        private async ValueTask InvokeOnFailureActionAsync(object action, Exception exception, object explicitState)
        {
            var invoker = _templateCache.GetOrAddOnFailureInvoker(action.GetType());
            if (invoker == null)
            {
                throw new InvalidOperationException("OnFailureAction signature is not supported or Invoke method not found.");
            }

            await invoker(action, exception, explicitState);
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

        private void InvokeAfterMatchAction(object action, object signalData, object explicitState)
        {
            var invoker = _templateCache.GetOrAddAfterMatchInvoker(action.GetType());
            if (invoker == null)
            {
                throw new InvalidOperationException("AfterMatchAction signature is not supported or Invoke method not found.");
            }

            invoker(action, signalData, explicitState);
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

        private static void SaveWaitStatesToMachineState(Wait wait, StateMachineObject stateMachineObject)
        {
            if (wait == null || stateMachineObject == null) return;

            stateMachineObject.WaitStatesObjects ??= new Dictionary<Guid, object>();

            // Save current wait's ExplicitState if not null and not already saved (deduplication)
            if (wait.ExplicitState != null && !stateMachineObject.WaitStatesObjects.ContainsKey(wait.Id))
            {
                stateMachineObject.WaitStatesObjects[wait.Id] = wait.ExplicitState;
            }

            // Recursively save child waits' states
            if (wait.ChildWaits != null)
            {
                foreach (var childWait in wait.ChildWaits)
                {
                    SaveWaitStatesToMachineState(childWait, stateMachineObject);
                }
            }
        }

        private async Task ExecuteCompensationAsync(
            CompensationWait compensationWait,
            WorkflowStateDto state,
            WorkflowContainer workflowInstance)
        {
            // Build command history from state
            var commandHistory = BuildCommandHistory(state.StateObject);

            // Filter commands by token (support multiple tokens)
            var commandsToCompensate = commandHistory
                .Where(cmd => cmd.Tokens != null && cmd.Tokens.Contains(compensationWait.Token))
                .Where(cmd => !cmd.IsCompensated) // Skip already compensated
                .OrderByDescending(cmd => cmd.ExecutionOrder) // LIFO order
                .ToList();

            // Execute compensation delegates
            foreach (var command in commandsToCompensate)
            {
                if (command.CompensationAction != null)
                {
                    try
                    {
                        // Invoke compensation with the saved result
                        var invoker = _templateCache.GetOrAddCompensationInvoker(command.CompensationAction.GetType());
                        if (invoker != null)
                        {
                            await invoker(command.CompensationAction, command.Result, command.ExplicitState);
                        }

                        // Mark as compensated
                        command.IsCompensated = true;
                    }
                    catch (Exception ex)
                    {
                        // Log compensation failure but continue with others
                        await workflowInstance.OnError($"Compensation failed for command {command.CommandType}: {ex.Message}", ex);
                    }
                }
            }

            // Update command history in state
            UpdateCommandHistoryInState(state.StateObject, commandHistory);
        }

        private List<CommandHistoryEntry> BuildCommandHistory(StateMachineObject stateObject)
        {
            // Extract command history from state using a well-known GUID
            var commandHistoryKey = new Guid("00000000-0000-0000-0000-000000000001"); // Reserved for command history

            if (stateObject.StateMachinesObjects?.TryGetValue(commandHistoryKey, out var historyObj) == true)
            {
                return historyObj as List<CommandHistoryEntry> ?? new List<CommandHistoryEntry>();
            }

            return new List<CommandHistoryEntry>();
        }

        private void UpdateCommandHistoryInState(StateMachineObject stateObject, List<CommandHistoryEntry> commandHistory)
        {
            stateObject.StateMachinesObjects ??= new Dictionary<Guid, object>();
            var commandHistoryKey = new Guid("00000000-0000-0000-0000-000000000001"); // Reserved for command history
            stateObject.StateMachinesObjects[commandHistoryKey] = commandHistory;
        }

        private bool IsWaitCancelled(Wait wait, HashSet<string> cancelledTokens)
        {
            if (cancelledTokens == null || cancelledTokens.Count == 0)
            {
                return false;
            }

            // Check if wait has cancel tokens through reflection
            var waitType = wait.GetType();
            var cancelTokensField = waitType.GetField("CancelTokens", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            if (cancelTokensField != null)
            {
                var tokens = cancelTokensField.GetValue(wait) as List<string>;
                if (tokens != null && tokens.Any(token => cancelledTokens.Contains(token)))
                {
                    return true;
                }
            }

            return false;
        }

        private async ValueTask InvokeCancelActionAsync(Wait wait)
        {
            if (wait.CancelAction != null)
            {
                try
                {
                    await wait.CancelAction();
                }
                catch (Exception ex)
                {
                    // Log but don't throw - cancellation callbacks should not block workflow
                    if (wait.WorkflowContainer != null)
                    {
                        await wait.WorkflowContainer.OnError($"Cancel action failed for wait {wait.WaitName}: {ex.Message}", ex);
                    }
                }
            }
        }

        // Helper class to track command execution history
        private class CommandHistoryEntry
        {
            public string CommandType { get; set; }
            public object Result { get; set; }
            public object ExplicitState { get; set; }
            public List<string> Tokens { get; set; }
            public object CompensationAction { get; set; }
            public bool IsCompensated { get; set; }
            public int ExecutionOrder { get; set; }
        }
    }
}
