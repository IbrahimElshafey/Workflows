using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <see cref="IImmediateCommandHandler{TCommand,TResult}"/> instances from the DI container
    /// by their registered handler key.
    /// </summary>
    public sealed class DiCommandHandlerFactory : ICommandHandlerFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<string, HandlerKeyEntry> _entries;

        public DiCommandHandlerFactory(IServiceProvider serviceProvider, IEnumerable<HandlerKeyEntry> entries)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _entries = entries?.ToDictionary(e => e.HandlerKey, StringComparer.OrdinalIgnoreCase)
                       ?? new Dictionary<string, HandlerKeyEntry>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns a <c>Func&lt;object, Task&lt;object&gt;&gt;</c> delegate for the registered handler key,
        /// compatible with the invocation pattern used by <c>ImmediateCommandProcessor</c>.
        /// </summary>
        public object GetHandler(string handlerKey)
        {
            if (!_entries.TryGetValue(handlerKey, out var entry))
                return null!;

            var handlerInterfaceType = typeof(IImmediateCommandHandler<,>)
                .MakeGenericType(entry.CommandType, entry.ResultType);

            Func<object, Task<object>> wrapper = async (commandObj) =>
            {
                var handler = _serviceProvider.GetRequiredService(handlerInterfaceType);

                // IImmediateCommandHandler<TCommand,TResult>.HandleAsync(TCommand, CancellationToken)
                var handleMethod = handlerInterfaceType.GetMethod(nameof(IImmediateCommandHandler<object, object>.HandleAsync));
                if (handleMethod == null)
                    throw new InvalidOperationException($"HandleAsync not found on {handlerInterfaceType}.");

                var valueTask = handleMethod.Invoke(handler, new[] { commandObj, CancellationToken.None });

                // ValueTask<TResult> → Task<TResult> → object
                var asTaskMethod = valueTask!.GetType().GetMethod("AsTask");
                if (asTaskMethod == null)
                    throw new InvalidOperationException("AsTask() not found on ValueTask.");

                var task = (Task)asTaskMethod.Invoke(valueTask, null)!;
                await task.ConfigureAwait(false);

                var resultProperty = task.GetType().GetProperty("Result");
                return resultProperty?.GetValue(task)!;
            };

            return wrapper;
        }
    }
}
