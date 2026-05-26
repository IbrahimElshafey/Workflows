using System;
using System.Reflection;

namespace Workflows.Runner.Helpers
{
    internal static class MethodResolver
    {
        public static MethodInfo? ResolveMethod(string methodFullPath)
        {
            if (string.IsNullOrWhiteSpace(methodFullPath))
                return null;

            int lastDot = methodFullPath.LastIndexOf('.');
            if (lastDot == -1)
                return null;

            string typeName = methodFullPath.Substring(0, lastDot);
            string methodName = methodFullPath.Substring(lastDot + 1);

            Type? type = Type.GetType(typeName);
            if (type == null)
            {
                // Fallback: search loaded assemblies for the type (e.g. if assembly is not referenced directly)
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(typeName);
                    if (type != null)
                        break;
                }
            }

            if (type == null)
                return null;

            return type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        }
    }
}
