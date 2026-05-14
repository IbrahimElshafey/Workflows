using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
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
        private readonly ConcurrentDictionary<Type, Action<object, object>> _afterMatchInvokers = new();

        public ObjectFactory GetOrAddWorkflowFactory(Type workflowType)
        {
            return _workflowFactories.GetOrAdd(workflowType, type => ActivatorUtilities.CreateFactory(type, Array.Empty<Type>()));
        }

        public Func<object, object> GetOrAddWorkflowInvoker(Type containerType, string methodName)
        {
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

        public Action<object, object> GetOrAddAfterMatchInvoker(Type actionType)
        {
            return _afterMatchInvokers.GetOrAdd(actionType, type =>
            {
                var method = type.GetMethod("Invoke");
                if (method == null) return null;

                var actionParam = Expression.Parameter(typeof(object), "action");
                var signalParam = Expression.Parameter(typeof(object), "signal");

                var parameters = method.GetParameters();
                Expression call;
                if (parameters.Length == 0)
                {
                    call = Expression.Call(Expression.Convert(actionParam, type), method);
                }
                else if (parameters.Length == 1)
                {
                    call = Expression.Call(Expression.Convert(actionParam, type), method, Expression.Convert(signalParam, parameters[0].ParameterType));
                }
                else
                {
                    return null;
                }

                var lambda = Expression.Lambda<Action<object, object>>(call, actionParam, signalParam);
                return lambda.CompileFast();
            });
        }

        public SignalTemplateCacheRecord GetOrAddSignal(string key, SignalTemplateCacheRecord record)
        {
            return _signalCache.GetOrAdd(key, record);
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
