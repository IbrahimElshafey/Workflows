using System;
using System.Reflection;
using Workflows.Abstraction.Helpers;

namespace Workflows.Runner.Helpers
{
    internal sealed class DelegateSerializer : IDelegateSerializer
    {
        public MethodInfo Deserialize(string methodFullPath)
        {
            throw new NotImplementedException();
        }

        public string Serialize(Delegate callback)
        {
            if (callback == null)
                return null;

            var owner = callback.Method.DeclaringType?.FullName;
            return string.IsNullOrWhiteSpace(owner)
                ? callback.Method.Name
                : $"{owner}.{callback.Method.Name}";
        }
    }
}
