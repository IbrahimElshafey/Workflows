using System;
using System.Collections.Generic;

namespace Workflows.Client.Internal
{
    /// <summary>
    /// Singleton registry that maps handler keys to their
    /// <see cref="CommandExecutorDescriptor"/> instances.
    /// Populated at startup via <c>AddCommandExecutor&lt;,,&gt;</c> and
    /// consumed at runtime by <see cref="CommandExecutorWorker"/>.
    /// </summary>
    internal sealed class CommandExecutorRegistry
    {
        private readonly Dictionary<string, CommandExecutorDescriptor> _map = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers a descriptor under the given handler key.
        /// Throws if the same key is registered twice.
        /// </summary>
        public void Register(string handlerKey, CommandExecutorDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(handlerKey))
                throw new ArgumentNullException(nameof(handlerKey));

            if (_map.ContainsKey(handlerKey))
                throw new InvalidOperationException(
                    $"A command executor is already registered for key '{handlerKey}'.");

            _map[handlerKey] = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        }

        /// <summary>
        /// Returns the descriptor for the given key, or <c>null</c> if not found.
        /// </summary>
        public CommandExecutorDescriptor? TryGet(string handlerKey)
            => _map.TryGetValue(handlerKey, out var d) ? d : null;
    }
}
