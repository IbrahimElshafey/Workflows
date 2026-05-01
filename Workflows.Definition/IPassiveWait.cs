using System;
using System.Collections.Generic;

namespace Workflows.Handler.BaseUse
{
    /// <summary>
    /// Marker interface for passive waits that react to external events without initiating side effects.
    /// Passive waits can be safely combined in groups with MatchAny() semantics.
    /// Examples: SignalWait, TimeWait, SubWorkflowWait
    /// </summary>
    public interface IPassiveWait
    {
        /// <summary>
        /// Token IDs that, when cancelled, will interrupt this passive wait.
        /// </summary>
        HashSet<string> CancelTokens { get; set; }

        /// <summary>
        /// Appends the given token ID to <see cref="CancelTokens"/>, ignoring null/empty strings and duplicates.
        /// When this token is cancelled the wait will be interrupted before the engine evaluates the incoming event.
        /// </summary>
        IPassiveWait WithCancelToken(string token);
    }
}
