using System;
using System.Linq.Expressions;

namespace Workflows.Abstraction.Runner
{
    /// <summary>
    /// Marker interface for a command that is dispatched to an external process.
    /// The runner suspends after dispatch. The orchestrator resumes it when an
    /// incoming result payload is matched by <see cref="MatchingFunction"/>.
    /// </summary>
    public interface IDeferredCommand<TInput, TResult>
    {
        /// <summary>
        /// Expression evaluated by the orchestrator to correlate an incoming
        /// callback result payload back to THIS command wait instance.
        /// </summary>
        Expression<Func<TInput, TResult, bool>> MatchingFunction { get; }
    }
}
