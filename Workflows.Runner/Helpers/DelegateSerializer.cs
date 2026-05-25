using System;
using System.Reflection;
using Workflows.Abstraction.Helpers;

namespace Workflows.Runner.Helpers
{
    internal sealed class DelegateSerializer : IDelegateSerializer
    {
        public MethodInfo Deserialize(string methodFullPath)
        {
            if (string.IsNullOrWhiteSpace(methodFullPath))
                return null;

            int lastDot = methodFullPath.LastIndexOf('.');
            if (lastDot == -1)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            var method = type.GetMethod(methodFullPath, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                            if (method != null)
                                return method;
                        }
                    }
                    catch
                    {
                    }
                }
                return null;
            }

            string typeName = methodFullPath.Substring(0, lastDot);
            string methodName = methodFullPath.Substring(lastDot + 1);

            Type targetType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    targetType = assembly.GetType(typeName);
                    if (targetType != null)
                        break;
                }
                catch
                {
                }
            }

            if (targetType == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            if (type.FullName == typeName || type.Name == typeName)
                            {
                                targetType = type;
                                break;
                            }
                        }
                        if (targetType != null)
                            break;
                    }
                    catch
                    {
                    }
                }
            }

            if (targetType == null)
                return null;

            return targetType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        }

        public string Serialize(Delegate callback)
        {
            if (callback == null)
                return null;

            var target = callback.Target;
            if (target != null && target.GetType().Name.Contains("Stateful"))
            {
                var delegateField = System.Linq.Enumerable.FirstOrDefault(
                    target.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                    f => typeof(Delegate).IsAssignableFrom(f.FieldType));

                if (delegateField != null)
                {
                    var underlyingDelegate = delegateField.GetValue(target) as Delegate;
                    if (underlyingDelegate != null)
                    {
                        callback = underlyingDelegate;
                    }
                }
            }

            var owner = callback.Method.DeclaringType?.FullName;
            return string.IsNullOrWhiteSpace(owner)
                ? callback.Method.Name
                : $"{owner}.{callback.Method.Name}";
        }
    }
}
