using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using FastExpressionCompiler;
using Microsoft.Extensions.DependencyInjection;

namespace Workflows.Runner.Cache
{
    internal class WorkflowTemplateCache
    {
        private readonly ConcurrentDictionary<string, SignalTemplateCacheRecord> _signalCache = new();
        private readonly ConcurrentDictionary<string, CommandTemplateCacheRecord> _commandCache = new();
        private readonly ConcurrentDictionary<string, GroupTemplateCacheRecord> _groupCache = new();
        private readonly ConcurrentDictionary<Type, ObjectFactory> _workflowFactories = new();
        private readonly ConcurrentDictionary<string, Func<object, object>> _workflowInvokers = new();
        private readonly ConcurrentDictionary<Type, Action<object, object, object>> _afterMatchInvokers = new();
        private readonly ConcurrentDictionary<Type, Action<object, object, object>> _onResultInvokers = new();
        private readonly ConcurrentDictionary<Type, Func<object, object, object, ValueTask>> _onFailureInvokers = new();
        private readonly ConcurrentDictionary<Type, Func<object, object, object, ValueTask>> _compensationInvokers = new();
        private readonly ConcurrentDictionary<Type, Func<object, object, object, ValueTask>> _cancelActionInvokers = new();
        private readonly ConcurrentDictionary<Type, Func<object, object, object, bool>> _groupFilterInvokers = new();
        private readonly ConcurrentDictionary<Type, Func<object, CommandWaitProperties>> _commandPropertiesExtractors = new();
        private readonly ConcurrentDictionary<Type, Func<object, IEnumerable<string>>> _cancelTokensExtractors = new();

        public class CommandWaitProperties
        {
            public object CommandData { get; set; }
            public object OnResultAction { get; set; }
            public object OnFailureAction { get; set; }
            public object CompensationAction { get; set; }
            public IEnumerable<string> Tokens { get; set; }
            public object ExplicitState { get; set; }
        }

        public ObjectFactory GetOrAddWorkflowFactory(Type workflowType)
        {
            return _workflowFactories.GetOrAdd(workflowType, type => ActivatorUtilities.CreateFactory(type, Array.Empty<Type>()));
        }

        public Func<object, object> GetOrAddWorkflowInvoker(Type containerType, string methodName)
        {
            if (string.IsNullOrEmpty(methodName)) return null;
            var key = $"{containerType.FullName}:{methodName}";
            return _workflowInvokers.GetOrAdd(key, _ =>
            {
                var method = containerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null) return null;

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var call = Expression.Call(Expression.Convert(instanceParam, containerType), method);
                var lambda = Expression.Lambda<Func<object, object>>(Expression.Convert(call, typeof(object)), instanceParam);
                return lambda.CompileFast();
            });
        }

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
                    // Action with no parameters
                    call = Expression.Call(Expression.Convert(actionParam, type), method);
                }
                else if (parameters.Length == 1)
                {
                    // Action<TSignal> - stateless
                    call = Expression.Call(Expression.Convert(actionParam, type), method, 
                        Expression.Convert(signalParam, parameters[0].ParameterType));
                }
                else
                {
                    // Unsupported signature (should not happen with StatefulAfterMatchInvoker wrapper)
                    return null;
                }

                var lambda = Expression.Lambda<Action<object, object, object>>(call, actionParam, signalParam, stateParam);
                return lambda.CompileFast();
            });
        }

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
                    // Action with no parameters
                    call = Expression.Call(Expression.Convert(actionParam, type), method);
                }
                else if (parameters.Length == 1)
                {
                    // Action<TResult> - stateless
                    call = Expression.Call(Expression.Convert(actionParam, type), method,
                        Expression.Convert(resultParam, parameters[0].ParameterType));
                }
                else
                {
                    // Unsupported signature
                    return null;
                }

                var lambda = Expression.Lambda<Action<object, object, object>>(call, actionParam, resultParam, stateParam);
                return lambda.CompileFast();
            });
        }

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
                    // Func<ValueTask> with no parameters
                    call = Expression.Call(Expression.Convert(actionParam, type), method);
                }
                else if (parameters.Length == 1)
                {
                    // Func<Exception, ValueTask> - stateless
                    call = Expression.Call(Expression.Convert(actionParam, type), method,
                        Expression.Convert(exceptionParam, parameters[0].ParameterType));
                }
                else
                {
                    // Unsupported signature
                    return null;
                }

                var lambda = Expression.Lambda<Func<object, object, object, ValueTask>>(call, actionParam, exceptionParam, stateParam);
                return lambda.CompileFast();
            });
        }

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
                    // Func<ValueTask> with no parameters
                    call = Expression.Call(Expression.Convert(actionParam, type), method);
                }
                else if (parameters.Length == 1)
                {
                    // Func<TResult, ValueTask> - stateless
                    call = Expression.Call(Expression.Convert(actionParam, type), method,
                        Expression.Convert(resultParam, parameters[0].ParameterType));
                }
                else
                {
                    // Unsupported signature
                    return null;
                }

                var lambda = Expression.Lambda<Func<object, object, object, ValueTask>>(call, actionParam, resultParam, stateParam);
                return lambda.CompileFast();
            });
        }

        public Func<object, object, object, ValueTask> GetOrAddCancelActionInvoker(Type actionType)
        {
            return _cancelActionInvokers.GetOrAdd(actionType, type =>
            {
                var method = type.GetMethod("Invoke");
                if (method == null) return null;

                var actionParam = Expression.Parameter(typeof(object), "action");
                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var stateParam = Expression.Parameter(typeof(object), "state");

                var parameters = method.GetParameters();
                Expression call;
                if (parameters.Length == 0)
                {
                    // Func<ValueTask> with no parameters
                    call = Expression.Call(Expression.Convert(actionParam, type), method);
                }
                else
                {
                    // Unsupported signature
                    return null;
                }

                var lambda = Expression.Lambda<Func<object, object, object, ValueTask>>(call, actionParam, instanceParam, stateParam);
                return lambda.CompileFast();
            });
        }

        public Func<object, object, object, bool> GetOrAddGroupFilterInvoker(Type filterType)
        {
            return _groupFilterInvokers.GetOrAdd(filterType, type =>
            {
                var method = type.GetMethod("Invoke");
                if (method == null) return null;

                var filterParam = Expression.Parameter(typeof(object), "filter");
                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var stateParam = Expression.Parameter(typeof(object), "state");

                var parameters = method.GetParameters();
                Expression call;
                if (parameters.Length == 0)
                {
                    // Func<bool> with no parameters
                    call = Expression.Call(Expression.Convert(filterParam, type), method);
                }
                else
                {
                    // Unsupported signature
                    return null;
                }

                var lambda = Expression.Lambda<Func<object, object, object, bool>>(call, filterParam, instanceParam, stateParam);
                return lambda.CompileFast();
            });
        }

        public SignalTemplateCacheRecord GetOrAddSignal(string key, SignalTemplateCacheRecord record)
        {
            return _signalCache.GetOrAdd(key, record);
        }

        public Func<object, CommandWaitProperties> GetOrAddCommandPropertiesExtractor(Type commandWaitType)
        {
            return _commandPropertiesExtractors.GetOrAdd(commandWaitType, type =>
            {
                var waitParam = Expression.Parameter(typeof(object), "wait");
                var typedWait = Expression.Convert(waitParam, type);

                var props = new[] { "CommandData", "OnResultAction", "OnFailureAction", "CompensationAction", "CompensationTokens", "Tokens" };
                var bindings = new List<MemberBinding>();

                var resultType = typeof(CommandWaitProperties);
                var explicitStateProp = type.GetProperty("ExplicitState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                     ?? type.BaseType?.GetProperty("ExplicitState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                foreach (var propName in props)
                {
                    var prop = type.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prop != null)
                    {
                        var targetPropName = (propName == "CompensationTokens" || propName == "Tokens") ? "Tokens" : propName;
                        bindings.Add(Expression.Bind(resultType.GetProperty(targetPropName), Expression.Convert(Expression.Property(typedWait, prop), typeof(object))));
                    }
                }

                if (explicitStateProp != null)
                {
                    bindings.Add(Expression.Bind(resultType.GetProperty("ExplicitState"), Expression.Convert(Expression.Property(typedWait, explicitStateProp), typeof(object))));
                }

                var body = Expression.MemberInit(Expression.New(resultType), bindings);
                var lambda = Expression.Lambda<Func<object, CommandWaitProperties>>(body, waitParam);
                return lambda.CompileFast();
            });
        }

        public Func<object, IEnumerable<string>> GetOrAddCancelTokensExtractor(Type waitType)
        {
            return _cancelTokensExtractors.GetOrAdd(waitType, type =>
            {
                var waitParam = Expression.Parameter(typeof(object), "wait");
                var typedWait = Expression.Convert(waitParam, type);

                var prop = type.GetProperty("CancelTokens", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var field = type.GetField("CancelTokens", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (prop == null && field == null) return _ => null;

                var member = prop != null ? (Expression)Expression.Property(typedWait, prop) : Expression.Field(typedWait, field);
                var body = Expression.Convert(member, typeof(IEnumerable<string>));
                var lambda = Expression.Lambda<Func<object, IEnumerable<string>>>(body, waitParam);
                return lambda.CompileFast();
            });
        }

        public SignalTemplateCacheRecord GetSignal(string key)
        {
            _signalCache.TryGetValue(key, out var record);
            return record;
        }

        public CommandTemplateCacheRecord GetOrAddCommand(string key, CommandTemplateCacheRecord record)
        {
            return _commandCache.GetOrAdd(key, record);
        }

        public CommandTemplateCacheRecord GetCommand(string key)
        {
            _commandCache.TryGetValue(key, out var record);
            return record;
        }

        public GroupTemplateCacheRecord GetOrAddGroup(string key, GroupTemplateCacheRecord record)
        {
            return _groupCache.GetOrAdd(key, record);
        }

        public GroupTemplateCacheRecord GetGroup(string key)
        {
            _groupCache.TryGetValue(key, out var record);
            return record;
        }
    }
}
