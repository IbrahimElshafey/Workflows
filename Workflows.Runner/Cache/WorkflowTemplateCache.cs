using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using FastExpressionCompiler;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Definition;

namespace Workflows.Runner.Cache
{
    internal class WorkflowTemplateCache
    {
        private readonly ConcurrentDictionary<string, SignalTemplateCacheRecord> _signalCache = new();
        private readonly ConcurrentDictionary<string, CommandTemplateCacheRecord> _commandCache = new();
        private readonly ConcurrentDictionary<string, GroupTemplateCacheRecord> _groupCache = new();
        private readonly ConcurrentDictionary<Type, ObjectFactory> _workflowFactories = new();
        private readonly ConcurrentDictionary<string, MethodInfo> _workflowMethods = new();
        private readonly ConcurrentDictionary<string, Func<object, IAsyncEnumerable<Wait>>> _workflowMethodDelegates = new();
        private readonly ConcurrentDictionary<Type, Action<object, object>> _afterMatchInvokerCache = new();

        public ObjectFactory GetOrAddWorkflowFactory(Type workflowType)
        {
            return _workflowFactories.GetOrAdd(workflowType, type => ActivatorUtilities.CreateFactory(type, Array.Empty<Type>()));
        }

        public MethodInfo GetOrAddWorkflowMethod(Type containerType, string methodName)
        {
            var key = $"{containerType.FullName}:{methodName}";
            return _workflowMethods.GetOrAdd(key, _ => containerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        }

        public Func<object, IAsyncEnumerable<Wait>> GetOrAddWorkflowMethodDelegate(Type containerType, string methodName)
        {
            var key = $"{containerType.FullName}:{methodName}";
            return _workflowMethodDelegates.GetOrAdd(key, _ =>
            {
                var method = containerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null) return null;

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var typedInstance = Expression.Convert(instanceParam, containerType);
                var call = Expression.Call(typedInstance, method);
                var castedCall = Expression.Convert(call, typeof(IAsyncEnumerable<Wait>));
                var lambda = Expression.Lambda<Func<object, IAsyncEnumerable<Wait>>>(castedCall, instanceParam);
                return lambda.CompileFast();
            });
        }

        public Action<object, object> GetOrAddAfterMatchInvoker(Type actionType)
        {
            return _afterMatchInvokerCache.GetOrAdd(actionType, type =>
            {
                var method = type.GetMethod("Invoke");
                if (method == null) return null;

                var actionParam = Expression.Parameter(typeof(object), "action");
                var dataParam = Expression.Parameter(typeof(object), "data");

                var typedAction = Expression.Convert(actionParam, type);
                var parameters = method.GetParameters();

                Expression call;
                if (parameters.Length == 0)
                {
                    call = Expression.Call(typedAction, method);
                }
                else if (parameters.Length == 1)
                {
                    var typedData = Expression.Convert(dataParam, parameters[0].ParameterType);
                    call = Expression.Call(typedAction, method, typedData);
                }
                else
                {
                    return null;
                }

                return Expression.Lambda<Action<object, object>>(call, actionParam, dataParam).CompileFast();
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
