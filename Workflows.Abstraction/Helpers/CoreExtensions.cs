using System.Reflection;

namespace Workflows.Handler.Helpers
{
    internal static class CoreExtensions
    {
        internal static BindingFlags DeclaredWithinTypeFlags() =>
        BindingFlags.DeclaredOnly |
        BindingFlags.Public |
        BindingFlags.NonPublic |
        BindingFlags.Static |
        BindingFlags.Instance;
    }
}
