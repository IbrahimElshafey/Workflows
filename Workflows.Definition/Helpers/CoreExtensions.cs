using System.Reflection;

namespace Workflows.Definition.Helpers
{
    public static class CoreExtensions
    {
        internal static BindingFlags DeclaredWithinTypeFlags() => BindingFlags.DeclaredOnly |
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Static |
            BindingFlags.Instance;
    }
}
