using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using FastExpressionCompiler;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline
{
    /// <inheritdoc />
    internal sealed class WorkflowHydrator : IWorkflowHydrator
    {
        private readonly IServiceProvider _serviceProvider;

        private static readonly ConcurrentDictionary<Type, ObjectFactory> _factories = new();
        private static readonly ConcurrentDictionary<string, Func<object, object?, object>> _invokers = new();

        public WorkflowHydrator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <inheritdoc />
        public WorkflowContainer CreateInstance(Type containerType)
        {
            var factory = _factories.GetOrAdd(containerType,
                t => ActivatorUtilities.CreateFactory(t, Array.Empty<Type>()));

            return (WorkflowContainer)factory(_serviceProvider, null);
        }

        /// <inheritdoc />
        public Func<object, object?, object> GetInvoker(Type containerType, string methodName, Type stateType)
        {
            var key = $"{containerType.FullName}:{methodName}:{stateType?.FullName ?? "null"}";
            return _invokers.GetOrAdd(key, _ =>
            {
                var method = containerType.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (method == null)
                    throw new InvalidOperationException(
                        $"Workflow method '{methodName}' not found on type '{containerType.FullName}'.");

                if (method.IsGenericMethodDefinition)
                {
                    method = method.MakeGenericMethod(stateType ?? typeof(object));
                }

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var stateParam = Expression.Parameter(typeof(object), "state");
                Expression call;
                if (method.GetParameters().Length == 1)
                {
                    var paramType = method.GetParameters()[0].ParameterType;
                    call = Expression.Call(
                        Expression.Convert(instanceParam, containerType),
                        method,
                        Expression.Convert(stateParam, paramType));
                }
                else
                {
                    call = Expression.Call(
                        Expression.Convert(instanceParam, containerType),
                        method);
                }

                var lambda = Expression.Lambda<Func<object, object?, object>>(
                    Expression.Convert(call, typeof(object)), instanceParam, stateParam);

                return lambda.CompileFast();
            });
        }
    }
}
