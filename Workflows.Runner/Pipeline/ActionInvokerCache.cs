using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FastExpressionCompiler;

namespace Workflows.Runner.Pipeline
{
    /// <summary>
    /// Caches compiled invokers for workflow action delegates (AfterMatch, OnResult, OnFailure, Compensation, Cancel).
    /// Each handler/evaluator can maintain its own instance or use a shared static instance.
    /// </summary>
    internal class ActionInvokerCache
    {
        private readonly ConcurrentDictionary<Type, Action<object, object, object>> _afterMatchInvokers = new();
        private readonly ConcurrentDictionary<Type, Action<object, object, object>> _onResultInvokers = new();
        private readonly ConcurrentDictionary<Type, Func<object, object, object, ValueTask>> _onFailureInvokers = new();
        private readonly ConcurrentDictionary<Type, Func<object, object, object, ValueTask>> _compensationInvokers = new();
        private readonly ConcurrentDictionary<Type, Func<object, ValueTask>> _cancelActionInvokers = new();

        /// <summary>
        /// Gets or creates a compiled invoker for AfterMatch actions.
        /// Signature: Action or Action&lt;TSignal&gt;
        /// </summary>
        public Action<object, object, object> GetOrAddAfterMatchInvoker(Type actionType)
        {
            return _afterMatchInvokers.GetOrAdd(actionType, type =>
            {
                var method = type.GetMethod("Invoke");
                if (method == null) return null;

                var actionParam = Expression.Parameter(typeof(object), "action");
                var signalParam = Expression.Parameter(typeof(object), "signal");
                var stateParam = Expression.Parameter(typeof(object), "state");

                var parameters = method.GetParameters();
                Expression call;
                if (parameters.Length == 0)
                {
                    call = Expression.Call(Expression.Convert(actionParam, type), method);
                }
                else if (parameters.Length == 1)
                {
                    call = Expression.Call(Expression.Convert(actionParam, type), method, 
                        Expression.Convert(signalParam, parameters[0].ParameterType));
                }
                else
                {
                    return null;
                }

                var lambda = Expression.Lambda<Action<object, object, object>>(call, actionParam, signalParam, stateParam);
                return lambda.CompileFast();
            });
        }

        /// <summary>
        /// Gets or creates a compiled invoker for OnResult actions.
        /// Signature: Action or Action&lt;TResult&gt;
        /// </summary>
        public Action<object, object, object> GetOrAddOnResultInvoker(Type actionType)
        {
            return _onResultInvokers.GetOrAdd(actionType, type =>
            {
                var method = type.GetMethod("Invoke");
                if (method == null) return null;

                var actionParam = Expression.Parameter(typeof(object), "action");
                var resultParam = Expression.Parameter(typeof(object), "result");
                var stateParam = Expression.Parameter(typeof(object), "state");

                var parameters = method.GetParameters();
                Expression call;
                if (parameters.Length == 0)
                {
                    call = Expression.Call(Expression.Convert(actionParam, type), method);
                }
                else if (parameters.Length == 1)
                {
                    call = Expression.Call(Expression.Convert(actionParam, type), method,
                        Expression.Convert(resultParam, parameters[0].ParameterType));
                }
                else
                {
                    return null;
                }

                var lambda = Expression.Lambda<Action<object, object, object>>(call, actionParam, resultParam, stateParam);
                return lambda.CompileFast();
            });
        }

        /// <summary>
        /// Gets or creates a compiled invoker for OnFailure actions.
        /// Signature: Func&lt;ValueTask&gt; or Func&lt;Exception, ValueTask&gt;
        /// </summary>
        public Func<object, object, object, ValueTask> GetOrAddOnFailureInvoker(Type actionType)
        {
            return _onFailureInvokers.GetOrAdd(actionType, type =>
            {
                var method = type.GetMethod("Invoke");
                if (method == null) return null;

                var actionParam = Expression.Parameter(typeof(object), "action");
                var exceptionParam = Expression.Parameter(typeof(object), "exception");
                var stateParam = Expression.Parameter(typeof(object), "state");

                var parameters = method.GetParameters();
                Expression call;
                if (parameters.Length == 0)
                {
                    call = Expression.Call(Expression.Convert(actionParam, type), method);
                }
                else if (parameters.Length == 1)
                {
                    call = Expression.Call(Expression.Convert(actionParam, type), method,
                        Expression.Convert(exceptionParam, parameters[0].ParameterType));
                }
                else
                {
                    return null;
                }

                var lambda = Expression.Lambda<Func<object, object, object, ValueTask>>(call, actionParam, exceptionParam, stateParam);
                return lambda.CompileFast();
            });
        }

        /// <summary>
        /// Gets or creates a compiled invoker for Compensation actions.
        /// Signature: Func&lt;ValueTask&gt; or Func&lt;TResult, ValueTask&gt;
        /// </summary>
        public Func<object, object, object, ValueTask> GetOrAddCompensationInvoker(Type actionType)
        {
            return _compensationInvokers.GetOrAdd(actionType, type =>
            {
                var method = type.GetMethod("Invoke");
                if (method == null) return null;

                var actionParam = Expression.Parameter(typeof(object), "action");
                var resultParam = Expression.Parameter(typeof(object), "result");
                var stateParam = Expression.Parameter(typeof(object), "state");

                var parameters = method.GetParameters();
                Expression call;
                if (parameters.Length == 0)
                {
                    call = Expression.Call(Expression.Convert(actionParam, type), method);
                }
                else if (parameters.Length == 1)
                {
                    call = Expression.Call(Expression.Convert(actionParam, type), method,
                        Expression.Convert(resultParam, parameters[0].ParameterType));
                }
                else
                {
                    return null;
                }

                var lambda = Expression.Lambda<Func<object, object, object, ValueTask>>(call, actionParam, resultParam, stateParam);
                return lambda.CompileFast();
            });
        }

        /// <summary>
        /// Gets or creates a compiled invoker for Cancel actions.
        /// Signature: Func&lt;ValueTask&gt;
        /// </summary>
        public Func<object, ValueTask> GetOrAddCancelActionInvoker(Type actionType)
        {
            return _cancelActionInvokers.GetOrAdd(actionType, type =>
            {
                var method = type.GetMethod("Invoke");
                if (method == null) return null;

                var actionParam = Expression.Parameter(typeof(object), "action");

                var call = Expression.Call(Expression.Convert(actionParam, type), method);
                var lambda = Expression.Lambda<Func<object, ValueTask>>(call, actionParam);
                return lambda.CompileFast();
            });
        }
    }
}
