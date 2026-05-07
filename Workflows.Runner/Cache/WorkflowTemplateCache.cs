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

        /// <summary>
        /// Caches compiled delegates for workflow entry-point methods.
        /// Replaces 'MethodInfo.Invoke' with a direct delegate call during workflow advancement.
        /// Expected impact: Reduces latency in the workflow 'hot path' by avoiding reflection overhead.
        /// </summary>
        private readonly ConcurrentDictionary<string, Func<object, IAsyncEnumerable<Wait>>> _workflowMethods = new();

        public ObjectFactory GetOrAddWorkflowFactory(Type workflowType)
        {
            return _workflowFactories.GetOrAdd(workflowType, type => ActivatorUtilities.CreateFactory(type, Array.Empty<Type>()));
        }

        public Func<object, IAsyncEnumerable<Wait>> GetOrAddWorkflowMethod(Type containerType, string methodName)
        {
            var key = $"{containerType.FullName}:{methodName}";
            return _workflowMethods.GetOrAdd(key, _ =>
            {
                var method = containerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null) return null;

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var typedInstance = Expression.Convert(instanceParam, containerType);
                var call = Expression.Call(typedInstance, method);

                // Ensure the result is cast to the expected interface type.
                var convertedCall = Expression.Convert(call, typeof(IAsyncEnumerable<Wait>));

                var lambda = Expression.Lambda<Func<object, IAsyncEnumerable<Wait>>>(convertedCall, instanceParam);
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
