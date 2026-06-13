using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.Runner;

namespace Workflows.Runner
{
    /// <summary>
    /// Describes a keyed registration of an immediate command handler.
    /// Stored as singletons so <see cref="DiCommandHandlerFactory"/> can discover them.
    /// </summary>
    public sealed class HandlerKeyEntry
    {
        public string HandlerKey { get; }
        public Type CommandType { get; }
        public Type ResultType { get; }

        public HandlerKeyEntry(string handlerKey, Type commandType, Type resultType)
        {
            HandlerKey = handlerKey ?? throw new ArgumentNullException(nameof(handlerKey));
            CommandType = commandType ?? throw new ArgumentNullException(nameof(commandType));
            ResultType = resultType ?? throw new ArgumentNullException(nameof(resultType));
        }
    }

    /// <summary>
    /// Default <see cref="ICommandHandlerFactory"/> implementation that resolves
    /// handlers from the DI container by their registered handler key, using keyed services
    /// first, and falling back to legacy entries.
    /// </summary>
    public sealed class DiCommandHandlerFactory : ICommandHandlerFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<string, HandlerKeyEntry> _entries;
        private readonly CommandRegistryOptions? _registryOptions;

        private static readonly ConcurrentDictionary<Type, Func<object, object, CancellationToken, Task<object>>> _handlerCache = new();
        private static readonly ConcurrentDictionary<Type, Func<object, IServiceProvider, object, object>> _delegateCache = new();

        public DiCommandHandlerFactory(
            IServiceProvider serviceProvider, 
            IEnumerable<HandlerKeyEntry> entries,
            CommandRegistryOptions? registryOptions = null)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _entries = entries?.ToDictionary(e => e.HandlerKey, StringComparer.OrdinalIgnoreCase)
                       ?? new Dictionary<string, HandlerKeyEntry>(StringComparer.OrdinalIgnoreCase);
            _registryOptions = registryOptions;
        }

        /// <summary>
        /// Returns a <c>Func&lt;object, Task&lt;object&gt;&gt;</c> delegate for the registered handler key.
        /// </summary>
        public object GetHandler(string handlerKey)
        {
            // Try new DI-registered Keyed Services path first (zero reflection on hot path)
            var metadata = _registryOptions?.Commands.GetValueOrDefault(handlerKey);
            if (metadata is { IsAsync: false })
            {
                var handlerInterfaceType = typeof(ICommandHandler<,>).MakeGenericType(metadata.InputType, metadata.OutputType);
                var keyedHandler = _serviceProvider.GetKeyedService(handlerInterfaceType, handlerKey);
                if (keyedHandler != null)
                {
                    var invoker = GetOrAddHandlerInvoker(metadata.InputType, metadata.OutputType);
                    Func<object, Task<object>> wrapper = async (commandObj) =>
                    {
                        var scopeKeyedHandler = _serviceProvider.GetRequiredKeyedService(handlerInterfaceType, handlerKey);
                        return await invoker(scopeKeyedHandler, commandObj, CancellationToken.None);
                    };
                    return wrapper;
                }

                var delegateType = typeof(Func<,,>).MakeGenericType(typeof(IServiceProvider), metadata.InputType, metadata.OutputType);
                var keyedDelegate = _serviceProvider.GetKeyedService(delegateType, handlerKey);
                if (keyedDelegate != null)
                {
                    var invoker = GetOrAddDelegateInvoker(metadata.InputType, metadata.OutputType);
                    Func<object, Task<object>> wrapper = (commandObj) =>
                    {
                        var scopeKeyedDelegate = _serviceProvider.GetRequiredKeyedService(delegateType, handlerKey);
                        var result = invoker(scopeKeyedDelegate, _serviceProvider, commandObj);
                        return Task.FromResult(result);
                    };
                    return wrapper;
                }
            }

            // Fallback to legacy HandlerKeyEntry path for backward compatibility
            if (!_entries.TryGetValue(handlerKey, out var entry))
                return null!;

            var legacyHandlerInterfaceType = typeof(IImmediateCommandHandler<,>)
                .MakeGenericType(entry.CommandType, entry.ResultType);

            Func<object, Task<object>> legacyWrapper = async (commandObj) =>
            {
                var handler = _serviceProvider.GetRequiredService(legacyHandlerInterfaceType);
                var handleMethod = legacyHandlerInterfaceType.GetMethod(nameof(IImmediateCommandHandler<object, object>.HandleAsync));
                if (handleMethod == null)
                    throw new InvalidOperationException($"HandleAsync not found on {legacyHandlerInterfaceType}.");

                var valueTask = handleMethod.Invoke(handler, new[] { commandObj, CancellationToken.None });
                var asTaskMethod = valueTask!.GetType().GetMethod("AsTask");
                if (asTaskMethod == null)
                    throw new InvalidOperationException("AsTask() not found on ValueTask.");

                var task = (Task)asTaskMethod.Invoke(valueTask, null)!;
                await task.ConfigureAwait(false);

                var resultProperty = task.GetType().GetProperty("Result");
                return resultProperty?.GetValue(task)!;
            };

            return legacyWrapper;
        }

        private static Func<object, object, CancellationToken, Task<object>> GetOrAddHandlerInvoker(Type inputType, Type outputType)
        {
            var handlerType = typeof(ICommandHandler<,>).MakeGenericType(inputType, outputType);
            return _handlerCache.GetOrAdd(handlerType, type =>
            {
                var handlerParam = Expression.Parameter(typeof(object), "handler");
                var commandParam = Expression.Parameter(typeof(object), "command");
                var tokenParam = Expression.Parameter(typeof(CancellationToken), "token");

                var castHandler = Expression.Convert(handlerParam, type);
                var castCommand = Expression.Convert(commandParam, inputType);

                var handleMethod = type.GetMethod("HandleAsync", new[] { inputType, typeof(CancellationToken) });
                var callExpr = Expression.Call(castHandler, handleMethod!, castCommand, tokenParam);

                var convertMethod = typeof(DiCommandHandlerFactory).GetMethod(nameof(ConvertValueTask), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.MakeGenericMethod(outputType);
                var convertCall = Expression.Call(convertMethod, callExpr);

                var lambda = Expression.Lambda<Func<object, object, CancellationToken, Task<object>>>(
                    convertCall, handlerParam, commandParam, tokenParam);

                return lambda.Compile();
            });
        }

        private static Func<object, IServiceProvider, object, object> GetOrAddDelegateInvoker(Type inputType, Type outputType)
        {
            var delegateType = typeof(Func<,,>).MakeGenericType(typeof(IServiceProvider), inputType, outputType);
            return _delegateCache.GetOrAdd(delegateType, type =>
            {
                var delegateParam = Expression.Parameter(typeof(object), "del");
                var spParam = Expression.Parameter(typeof(IServiceProvider), "sp");
                var commandParam = Expression.Parameter(typeof(object), "command");

                var castDelegate = Expression.Convert(delegateParam, type);
                var castCommand = Expression.Convert(commandParam, inputType);

                var invokeMethod = type.GetMethod("Invoke", new[] { typeof(IServiceProvider), inputType });
                var callExpr = Expression.Call(castDelegate, invokeMethod!, spParam, castCommand);

                var castResult = Expression.Convert(callExpr, typeof(object));

                var lambda = Expression.Lambda<Func<object, IServiceProvider, object, object>>(
                    castResult, delegateParam, spParam, commandParam);

                return lambda.Compile();
            });
        }

        private static async Task<object> ConvertValueTask<T>(ValueTask<T> valueTask)
        {
            var result = await valueTask.ConfigureAwait(false);
            return result!;
        }
    }
}
