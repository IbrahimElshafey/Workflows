using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using FastExpressionCompiler;
using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Runner.Pipeline.Matchers
{
    /// <summary>
    /// Evaluates deferred command results on integration callback return.
    /// Maps the result to the target CommandWait and runs the OnResultAction lambda to update variables.
    /// </summary>
    internal class DeferredCommandMatcher : WorkflowWaitMatcher
    {
        private static readonly ActionInvokerCache _invokerCache = new();
        private static readonly ConcurrentDictionary<Type, CommandWaitAccessor> _accessorCache = new();

        public override async Task<bool> MatchAsync(WorkflowExecutionContext context)
        {
            var commandWaitDto = context.TriggeringWaitDto as CommandWaitDto;
            if (commandWaitDto == null)
            {
                throw new InvalidOperationException("DeferredCommandEvaluator requires a CommandWaitDto.");
            }

            var commandWait = context.TriggeringWait as Definition.ICommandWait;
            if (commandWait == null)
            {
                throw new InvalidOperationException("Triggering wait could not be mapped to ICommandWait.");
            }

            var result = context.CommandResult;

            // Get or create compiled accessor for this command wait type
            var accessor = GetOrCreateAccessor(commandWait.GetType());

            var onResultAction = accessor.GetOnResultAction(commandWait);
            var onFailureAction = accessor.GetOnFailureAction(commandWait);
            var explicitState = accessor.GetExplicitState(commandWait);

            // Handle failure scenarios
            if (result is Exception exception)
            {
                // Result is an exception - invoke OnFailureAction if present
                if (onFailureAction != null)
                {
                    await InvokeOnFailureActionAsync(onFailureAction, exception, explicitState);
                }
                else
                {
                    // No failure handler - let exception propagate to workflow error handling
                    if (context.WorkflowInstance != null)
                    {
                        await context.WorkflowInstance.OnError(
                            $"Deferred command failed: {exception.Message}", exception);
                    }
                }
            }
            else
            {
                // Success - invoke OnResultAction
                if (onResultAction != null)
                {
                    InvokeOnResultAction(onResultAction, result, explicitState);
                }
            }

            return true;
        }

        private void InvokeOnResultAction(object action, object result, object explicitState)
        {
            var invoker = _invokerCache.GetOrAddOnResultInvoker(action.GetType());
            if (invoker == null)
            {
                throw new InvalidOperationException("OnResultAction signature is not supported or Invoke method not found.");
            }

            invoker(action, result, explicitState);
        }

        private async Task InvokeOnFailureActionAsync(object action, Exception exception, object explicitState)
        {
            var invoker = _invokerCache.GetOrAddOnFailureInvoker(action.GetType());
            if (invoker == null)
            {
                throw new InvalidOperationException("OnFailureAction signature is not supported or Invoke method not found.");
            }

            await invoker(action, exception, explicitState);
        }

        private static CommandWaitAccessor GetOrCreateAccessor(Type commandWaitType)
        {
            return _accessorCache.GetOrAdd(commandWaitType, type => new CommandWaitAccessor(type));
        }

        /// <summary>
        /// Cached compiled property accessors for a specific CommandWait type.
        /// Eliminates reflection overhead on every deferred command evaluation.
        /// </summary>
        private class CommandWaitAccessor
        {
            private readonly Func<object, object> _onResultActionGetter;
            private readonly Func<object, object> _onFailureActionGetter;
            private readonly Func<object, object> _explicitStateGetter;

            public CommandWaitAccessor(Type commandWaitType)
            {
                // Compile property getters once
                _onResultActionGetter = CompilePropertyGetter(commandWaitType, "OnResultAction", 
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _onFailureActionGetter = CompilePropertyGetter(commandWaitType, "OnFailureAction", 
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _explicitStateGetter = CompilePropertyGetter(commandWaitType.BaseType, "ExplicitState", 
                    BindingFlags.Instance | BindingFlags.Public);
            }

            public object GetOnResultAction(object commandWait) => _onResultActionGetter?.Invoke(commandWait);
            public object GetOnFailureAction(object commandWait) => _onFailureActionGetter?.Invoke(commandWait);
            public object GetExplicitState(object commandWait) => _explicitStateGetter?.Invoke(commandWait);

            private static Func<object, object> CompilePropertyGetter(Type type, string propertyName, BindingFlags bindingFlags)
            {
                if (type == null) return null;

                var property = type.GetProperty(propertyName, bindingFlags);
                if (property == null) return null;

                var parameter = Expression.Parameter(typeof(object), "instance");
                var convert = Expression.Convert(parameter, type);
                var getProperty = Expression.Property(convert, property);
                var convertResult = Expression.Convert(getProperty, typeof(object));
                var lambda = Expression.Lambda<Func<object, object>>(convertResult, parameter);

                return lambda.CompileFast();
            }
        }
    }
}

