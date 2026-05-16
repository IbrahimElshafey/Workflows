using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Runner.Cache;

namespace Workflows.Runner.Pipeline.Handlers
{
    /// <summary>
    /// Handles immediate command execution.
    /// Resolves the transient side-effect implementation from ICommandHandlerFactory
    /// and executes it instantly in RAM. Returns true to advance again in cycle.
    /// </summary>
    internal class ImmediateCommandHandler : WorkflowWaitHandler
    {
        private readonly ICommandHandlerFactory _commandHandlerFactory;
        private readonly WorkflowTemplateCache _templateCache;

        public ImmediateCommandHandler(
            ICommandHandlerFactory commandHandlerFactory,
            WorkflowTemplateCache templateCache)
        {
            _commandHandlerFactory = commandHandlerFactory ?? throw new ArgumentNullException(nameof(commandHandlerFactory));
            _templateCache = templateCache ?? throw new ArgumentNullException(nameof(templateCache));
        }

        public override async Task<bool> HandleAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var commandWait = yieldedWait as Definition.ICommandWait;
            if (commandWait == null)
            {
                throw new InvalidOperationException("ImmediateCommandHandler requires an ICommandWait.");
            }

            // Get command wait properties via reflection
            var commandWaitType = commandWait.GetType();
            var commandDataProperty = commandWaitType.GetProperty("CommandData",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var onResultActionProperty = commandWaitType.GetProperty("OnResultAction",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var onFailureActionProperty = commandWaitType.GetProperty("OnFailureAction",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var compensationActionProperty = commandWaitType.GetProperty("CompensationAction",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var tokensProperty = commandWaitType.GetProperty("Tokens",
                BindingFlags.Instance | BindingFlags.NonPublic);

            var commandData = commandDataProperty?.GetValue(commandWait);
            var onResultAction = onResultActionProperty?.GetValue(commandWait);
            var onFailureAction = onFailureActionProperty?.GetValue(commandWait);
            var compensationAction = compensationActionProperty?.GetValue(commandWait);
            var tokens = tokensProperty?.GetValue(commandWait) as List<string>;
            var explicitState = ((Wait)commandWait).ExplicitState;

            try
            {
                // Execute command through handler factory
                var handler = _commandHandlerFactory.GetHandler(commandWait.GetType().Name);
                object result = null;

                if (handler != null)
                {
                    // Handler is a Func<TCommand, Task<TResult>> - invoke it dynamically
                    var handlerType = handler.GetType();
                    var invokeMethod = handlerType.GetMethod("Invoke");
                    if (invokeMethod != null)
                    {
                        var task = invokeMethod.Invoke(handler, new[] { commandData }) as Task;
                        if (task != null)
                        {
                            await task.ConfigureAwait(false);
                            var resultProperty = task.GetType().GetProperty("Result");
                            result = resultProperty?.GetValue(task);
                        }
                    }
                }

                // Track command execution for compensation
                TrackCommandExecution(
                    context.ActiveState,
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

                // Return true - active wait, continue execution loop
                return true;
            }
            catch (Exception ex)
            {
                // Invoke OnFailure callback if present
                if (onFailureAction != null)
                {
                    await InvokeOnFailureActionAsync(onFailureAction, ex, explicitState);
                }

                // For immediate commands, we propagate the exception
                throw new InvalidOperationException($"Immediate command execution failed: {ex.Message}", ex);
            }
        }

        private void TrackCommandExecution(
            WorkflowStateObject stateObject,
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

        private List<CommandHistoryEntry> BuildCommandHistory(WorkflowStateObject stateObject)
        {
            var commandHistoryKey = new Guid("00000000-0000-0000-0000-000000000001");

            if (stateObject.StateMachinesObjects?.TryGetValue(commandHistoryKey, out var historyObj) == true)
            {
                return historyObj as List<CommandHistoryEntry> ?? new List<CommandHistoryEntry>();
            }

            return new List<CommandHistoryEntry>();
        }

        private void UpdateCommandHistoryInState(WorkflowStateObject stateObject, List<CommandHistoryEntry> commandHistory)
        {
            stateObject.StateMachinesObjects ??= new Dictionary<Guid, object>();
            var commandHistoryKey = new Guid("00000000-0000-0000-0000-000000000001");
            stateObject.StateMachinesObjects[commandHistoryKey] = commandHistory;
        }

        private void InvokeOnResultAction(object action, object result, object explicitState)
        {
            var invoker = _templateCache.GetOrAddOnResultInvoker(action.GetType());
            if (invoker == null)
            {
                throw new InvalidOperationException("OnResultAction signature is not supported.");
            }

            invoker(action, result, explicitState);
        }

        private async ValueTask InvokeOnFailureActionAsync(object action, Exception exception, object explicitState)
        {
            var invoker = _templateCache.GetOrAddOnFailureInvoker(action.GetType());
            if (invoker == null)
            {
                throw new InvalidOperationException("OnFailureAction signature is not supported.");
            }

            await invoker(action, exception, explicitState);
        }
    }

    internal class CommandHistoryEntry
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
