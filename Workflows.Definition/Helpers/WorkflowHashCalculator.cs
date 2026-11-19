using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Workflows.Definition.Helpers
{
    /// <summary>
    /// Produces a stable, recompile-safe hash key for a workflow wait callback.
    /// The hash is derived from the normalised match-expression text, the caller method
    /// name, and the signal/command identifier — none of which are compiler-generated.
    /// </summary>
    public static class WorkflowHashCalculator
    {
        /// <summary>
        /// Calculates a deterministic SHA-256 hash key.
        /// </summary>
        /// <param name="expressionText">
        ///   The raw text of the lambda expression captured via
        ///   <c>[CallerArgumentExpression]</c>.  May be <see langword="null"/> for
        ///   MatchAny waits — in that case only <paramref name="callerName"/> and
        ///   <paramref name="identifier"/> are combined.
        /// </param>
        /// <param name="callerName">
        ///   The workflow method name captured via <c>[CallerMemberName]</c>.
        /// </param>
        /// <param name="identifier">
        ///   The signal identifier or command identifier registered for this wait.
        /// </param>
        /// <returns>A URL-safe base-64 string that is stable across recompilations.</returns>
        public static string CalculateHash(string? expressionText, string callerName, string identifier)
        {
            // 1. Normalise: strip all whitespace so formatting differences are ignored.
            //    e.g.  "x => x.Id == 1"  and  "x=>x.Id==1"  produce the same hash.
            var normalized = expressionText is not null
                ? Regex.Replace(expressionText, @"\s+", "")
                : string.Empty;

            // 2. Combine into a single deterministic raw string.
            var raw = $"{normalized}_{callerName}_{identifier}";

            // 3. SHA-256 hash → base-64.
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return Convert.ToBase64String(bytes);
        }
    }
}
