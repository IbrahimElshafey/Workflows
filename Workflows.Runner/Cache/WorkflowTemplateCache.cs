using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
        private readonly ConcurrentDictionary<Type, Action<object, object>> _afterMatchActionInvokers = new();
        private readonly ConcurrentDictionary<string, CommandTemplateCacheRecord> _commandCache = new();
        private readonly ConcurrentDictionary<string, GroupTemplateCacheRecord> _groupCache = new();
        private readonly ConcurrentDictionary<Type, ObjectFactory> _workflowFactories = new();
        private readonly ConcurrentDictionary<string, Func<object, object, IAsyncEnumerable<Wait>>> _workflowInvokers = new();

        public ObjectFactory GetOrAddWorkflowFactory(Type workflowType)
        {
            return _workflowFactories.GetOrAdd(workflowType, type => ActivatorUtilities.CreateFactory(type, Array.Empty<Type>()));
        }

        public Func<object, object, IAsyncEnumerable<Wait>> GetOrAddWorkflowInvoker(Type containerType, string methodName)
        {
            var key = $"{containerType.FullName}:{methodName}";
            return _workflowInvokers.GetOrAdd(key, _ =>
            {
                var method = containerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null) return null;

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var argParam = Expression.Parameter(typeof(object), "arg");

                var typedInstance = Expression.Convert(instanceParam, containerType);
                var call = Expression.Call(typedInstance, method);

                return Expression.Lambda<Func<object, object, IAsyncEnumerable<Wait>>>(call, instanceParam, argParam).CompileFast();
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

        public Action<object, object> GetOrAddAfterMatchInvoker(Type actionType, Func<Type, Action<object, object>> factory)
        {
            return _afterMatchActionInvokers.GetOrAdd(actionType, factory);
        }
    }
}
