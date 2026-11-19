using System;
using System.Collections.Concurrent;

namespace Workflows.Runner
{
    /// <summary>
    /// Thread-safe, singleton implementation of <see cref="ICallbackRegistry"/>.
    /// Uses a <see cref="ConcurrentDictionary{TKey,TValue}"/> so reads are lock-free
    /// and writes are safe from multiple concurrent first-run executions.
    /// </summary>
    internal sealed class CallbackRegistry : ICallbackRegistry
    {
        private readonly ConcurrentDictionary<string, Delegate> _store = new(StringComparer.Ordinal);

        /// <inheritdoc/>
        public void Register(string key, Delegate callback)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            var unwrapped = UnwrapDelegate(callback);

            // Always update to the latest delegate.
            // On resume, the workflow re-executes Run() to rebuild its waits, generating fresh
            // delegate instances (with the new container as `this`). Overwriting ensures the
            // registry always holds the most recently registered (i.e. freshest) delegate.
            _store[key] = unwrapped;
        }

        public static Delegate UnwrapDelegate(Delegate @delegate)
        {
            if (@delegate == null) return null!;
            var target = @delegate.Target;
            if (target != null && target.GetType().Name.Contains("Stateful"))
            {
                var delegateField = System.Linq.Enumerable.FirstOrDefault(
                    target.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public),
                    f => typeof(Delegate).IsAssignableFrom(f.FieldType));

                if (delegateField != null)
                {
                    var underlyingDelegate = delegateField.GetValue(target) as Delegate;
                    if (underlyingDelegate != null)
                    {
                        return UnwrapDelegate(underlyingDelegate);
                    }
                }
            }
            return @delegate;
        }

        /// <inheritdoc/>
        public bool TryGet(string key, out Delegate? callback)
        {
            if (string.IsNullOrEmpty(key))
            {
                callback = null;
                return false;
            }
            return _store.TryGetValue(key, out callback);
        }

        /// <inheritdoc/>
        public bool Contains(string key) =>
            !string.IsNullOrEmpty(key) && _store.ContainsKey(key);
    }
}
