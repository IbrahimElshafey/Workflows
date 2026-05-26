using System;

namespace Workflows.Runner
{
    /// <summary>
    /// Thread-safe store for live callback delegates, keyed by their deterministic hash.
    /// Populated during workflow execution (first run) and queried on every subsequent
    /// resume — eliminating all reflection-based delegate resolution at runtime.
    /// </summary>
    public interface ICallbackRegistry
    {
        /// <summary>Registers a delegate under the given <paramref name="key"/>.</summary>
        void Register(string key, Delegate callback);

        /// <summary>
        /// Attempts to retrieve the delegate previously registered under <paramref name="key"/>.
        /// </summary>
        bool TryGet(string key, out Delegate? callback);

        /// <summary>Returns <see langword="true"/> if <paramref name="key"/> is already stored.</summary>
        bool Contains(string key);
    }
}
