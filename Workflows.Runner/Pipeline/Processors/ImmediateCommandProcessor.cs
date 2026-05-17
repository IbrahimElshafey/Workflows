using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using FastExpressionCompiler;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Processors
{
    /// <summary>
    /// Handles immediate command execution.
    /// Resolves the transient side-effect implementation from ICommandHandlerFactory
    /// and executes it instantly in RAM. Returns true to advance again in cycle.
    /// </summary>
    internal class ImmediateCommandProcessor : WorkflowWaitProcessor
    {
        private readonly ICommandHandlerFactory _commandHandlerFactory;
        private static readonly ActionInvokerCache _invokerCache = new();
        private static readonly ConcurrentDictionary<Type, CommandWaitAccessor> _accessorCache = new();

        public ImmediateCommandProcessor(ICommandHandlerFactory commandHandlerFactory)
        {
            _commandHandlerFactory = commandHandlerFactory ?? throw new ArgumentNullException(nameof(commandHandlerFactory));
        }

        public override async Task<bool> ProcessAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var commandWait = yieldedWait as Definition.ICommandWait;
            if (commandWait == null)
            {
                throw new InvalidOperationException("ImmediateCommandProcessor requires an ICommandWait.");
            }

            // Get or create compiled accessor for this command wait type
            var accessor = GetOrCreateAccessor(commandWait.GetType());

            var commandData = accessor.GetCommandData(commandWait);
            var onResultAction = accessor.GetOnResultAction(commandWait);
            var onFailureAction = accessor.GetOnFailureAction(commandWait);
            var compensationAction = accessor.GetCompensationAction(commandWait);
            var tokens = accessor.GetTokens(commandWait);
            var explicitState = accessor.GetExplicitState(commandWait);

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
                    context.WorkflowState.StateObject,
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
            var invoker = _invokerCache.GetOrAddOnResultInvoker(action.GetType());
            if (invoker == null)
            {
                throw new InvalidOperationException("OnResultAction signature is not supported.");
            }

            invoker(action, result, explicitState);
        }

        private async ValueTask InvokeOnFailureActionAsync(object action, Exception exception, object explicitState)
        {
            var invoker = _invokerCache.GetOrAddOnFailureInvoker(action.GetType());
            if (invoker == null)
            {
                throw new InvalidOperationException("OnFailureAction signature is not supported.");
            }

            await invoker(action, exception, explicitState);
        }

        private static CommandWaitAccessor GetOrCreateAccessor(Type commandWaitType)
        {
            return _accessorCache.GetOrAdd(commandWaitType, type => new CommandWaitAccessor(type));
        }

        /// <summary>
        /// Cached compiled property accessors for a specific CommandWait type.
        /// Eliminates reflection overhead on every immediate command execution.
        /// </summary>
        private class CommandWaitAccessor
        {
            private readonly Func<object, object> _commandDataGetter;
            private readonly Func<object, object> _onResultActionGetter;
            private readonly Func<object, object> _onFailureActionGetter;
            private readonly Func<object, object> _compensationActionGetter;
            private readonly Func<object, List<string>> _tokensGetter;
            private readonly Func<object, object> _explicitStateGetter;

            public CommandWaitAccessor(Type commandWaitType)
            {
                // Compile property getters once
                _commandDataGetter = CompilePropertyGetter<object>(commandWaitType, "CommandData", 
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _onResultActionGetter = CompilePropertyGetter<object>(commandWaitType, "OnResultAction", 
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _onFailureActionGetter = CompilePropertyGetter<object>(commandWaitType, "OnFailureAction", 
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _compensationActionGetter = CompilePropertyGetter<object>(commandWaitType, "CompensationAction", 
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _tokensGetter = CompilePropertyGetter<List<string>>(commandWaitType, "Tokens", 
                    BindingFlags.Instance | BindingFlags.NonPublic);

                // ExplicitState is on the base Wait type
                _explicitStateGetter = CompilePropertyGetter<object>(typeof(Wait), "ExplicitState", 
                    BindingFlags.Instance | BindingFlags.Public);
            }

            public object GetCommandData(object commandWait) => _commandDataGetter?.Invoke(commandWait);
            public object GetOnResultAction(object commandWait) => _onResultActionGetter?.Invoke(commandWait);
            public object GetOnFailureAction(object commandWait) => _onFailureActionGetter?.Invoke(commandWait);
            public object GetCompensationAction(object commandWait) => _compensationActionGetter?.Invoke(commandWait);
            public List<string> GetTokens(object commandWait) => _tokensGetter?.Invoke(commandWait);
            public object GetExplicitState(object commandWait) => _explicitStateGetter?.Invoke(commandWait);

            private static Func<object, TResult> CompilePropertyGetter<TResult>(Type type, string propertyName, BindingFlags bindingFlags)
            {
                if (type == null) return null;

                var property = type.GetProperty(propertyName, bindingFlags);
                if (property == null) return null;

                var parameter = Expression.Parameter(typeof(object), "instance");
                var convert = Expression.Convert(parameter, type);
                var getProperty = Expression.Property(convert, property);
                var convertResult = Expression.Convert(getProperty, typeof(TResult));
                var lambda = Expression.Lambda<Func<object, TResult>>(convertResult, parameter);

                return lambda.CompileFast();
            }
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

