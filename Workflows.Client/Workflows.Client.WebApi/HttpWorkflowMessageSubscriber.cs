using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Workflows.Communication.Abstraction;

namespace Workflows.Client.WebApi
{
    /// <summary>
    /// Implements <see cref="IMessageSubscriber"/> for receiving HTTP-based message notifications.
    /// Handlers are registered in memory, and invoked when the Web API endpoint matches the message type.
    /// </summary>
    public class HttpWorkflowMessageSubscriber : IMessageSubscriber
    {
        private readonly ConcurrentDictionary<Type, Func<object, Task>> _handlers = new();

        public void Subscribe<T>(Func<T, Task> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            _handlers[typeof(T)] = async obj =>
            {
                if (obj is T msg)
                {
                    await handler(msg).ConfigureAwait(false);
                }
                else
                {
                    throw new ArgumentException(
                        $"Handler expected message of type {typeof(T).Name} but received {obj?.GetType().Name}");
                }
            };
        }

        /// <summary>
        /// Routes an incoming message to the registered handler.
        /// </summary>
        public async Task HandleMessageAsync(Type type, object message)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (message == null) throw new ArgumentNullException(nameof(message));

            if (_handlers.TryGetValue(type, out var handler))
            {
                await handler(message).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _handlers.Clear();
        }
    }
}
