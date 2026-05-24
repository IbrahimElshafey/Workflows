using System;
using System.Threading;
using System.Threading.Tasks;

namespace Workflows.Client.Internal
{
    /// <summary>
    /// Captures all metadata needed to locate and invoke a registered
    /// <see cref="ICommandExecutor{TCommand,TResult}"/> at runtime without
    /// re-resolving generic types on every dispatch.
    /// </summary>
    internal sealed class CommandExecutorDescriptor
    {
        /// <summary>The <c>TCommand</c> type, used for deserialisation.</summary>
        public Type CommandType { get; init; }

        /// <summary>The <c>TResult</c> type (informational / future use).</summary>
        public Type ResultType { get; init; }

        /// <summary>
        /// Pre-built adapter that resolves the executor from a scoped
        /// <see cref="IServiceProvider"/>, invokes <c>ExecuteAsync</c>, and
        /// returns the result boxed as <c>object</c>.
        /// Compiled once at registration time to avoid runtime reflection cost.
        /// </summary>
        public Func<IServiceProvider, object, CancellationToken, Task<object>> Execute { get; init; }
    }
}
