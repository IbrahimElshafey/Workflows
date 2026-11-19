using System;
using System.Runtime.CompilerServices;

namespace Workflows.Definition.Helpers
{
    /// <summary>
    /// Rejects wait-state values that cannot survive a persist/resume round-trip.
    /// </summary>
    internal static class StateGuard
    {
        public static void EnsurePersistable(object state)
        {
            if (state == null)
                return;

            var type = state.GetType();
            if (!IsAnonymousType(type))
                return;

            throw new ArgumentException(
                $"Anonymous type '{type.Name}' cannot be used as workflow wait state. " +
                "The compiler generates it without a constructor a deserializer can use, so the value would be lost " +
                "once the workflow resumes. Declare a named class or record for the state instead.",
                nameof(state));
        }

        private static bool IsAnonymousType(Type type)
        {
            return type.Name.Contains("AnonymousType")
                && (type.Name.StartsWith("<>", StringComparison.Ordinal)
                    || type.Name.StartsWith("VB$", StringComparison.Ordinal))
                && Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute), false);
        }
    }
}
