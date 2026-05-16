using System.Collections.Generic;
using System.Linq;

namespace Workflows.Abstraction.DTOs
{
    /// <summary>
    /// Extension methods for working with cancellation history.
    /// </summary>
    public static class CancellationHistoryExtensions
    {
        /// <summary>
        /// Gets all cancelled tokens from the cancellation history.
        /// </summary>
        public static HashSet<string> GetCancelledTokens(this List<CancellationHistoryEntry> history)
        {
            if (history == null || history.Count == 0)
            {
                return new HashSet<string>();
            }

            return new HashSet<string>(history.Select(entry => entry.Token));
        }

        /// <summary>
        /// Checks if a specific token has been cancelled.
        /// </summary>
        public static bool IsTokenCancelled(this List<CancellationHistoryEntry> history, string token)
        {
            return history?.Any(entry => entry.Token == token) == true;
        }
    }
}
