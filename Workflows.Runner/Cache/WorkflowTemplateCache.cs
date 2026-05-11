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
        private readonly ConcurrentDictionary<string, MethodInfo> _workflowMethods = new();
        private readonly ConcurrentDictionary<Type, Action<object, object>> _afterMatchActionCache = new();
        private readonly ConcurrentDictionary<string, Func<object, object>> _workflowMethodDelegates = new();

        public ObjectFactory GetOrAddWorkflowFactory(Type workflowType)
        {
            return _workflowFactories.GetOrAdd(workflowType, type => ActivatorUtilities.CreateFactory(type, Array.Empty<Type>()));
        }

        public MethodInfo GetOrAddWorkflowMethod(Type containerType, string methodName)
        {
            var key = $"{containerType.FullName}:{methodName}";
            return _workflowMethods.GetOrAdd(key, _ => containerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        }

        public Func<object, object> GetOrAddWorkflowMethodDelegate(Type containerType, MethodInfo methodInfo)
        {
            var key = $"{containerType.FullName}:{methodInfo.Name}";
            return _workflowMethodDelegates.GetOrAdd(key, _ =>
            {
                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var body = Expression.Call(Expression.Convert(instanceParam, containerType), methodInfo);
                Expression convertedBody = methodInfo.ReturnType == typeof(void)
                    ? Expression.Block(body, Expression.Constant(null))
                    : Expression.Convert(body, typeof(object));

                var lambda = Expression.Lambda<Func<object, object>>(convertedBody, instanceParam);
                return lambda.CompileFast();
            });
        }

        public Action<object, object> GetOrAddAfterMatchInvoker(Type actionType)
        {
            return _afterMatchActionCache.GetOrAdd(actionType, type =>
            {
                var method = type.GetMethod("Invoke");
                if (method == null) return null;

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var dataParam = Expression.Parameter(typeof(object), "data");

                var parameters = method.GetParameters();
                Expression body;
                if (parameters.Length == 0)
                {
                    body = Expression.Call(Expression.Convert(instanceParam, type), method);
                }
                else
                {
                    body = Expression.Call(Expression.Convert(instanceParam, type), method, Expression.Convert(dataParam, parameters[0].ParameterType));
                }

                var lambda = Expression.Lambda<Action<object, object>>(body, instanceParam, dataParam);
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
