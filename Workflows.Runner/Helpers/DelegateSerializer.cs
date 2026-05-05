using System;

namespace Workflows.Runner.Helpers
{
    internal sealed class DelegateSerializer : IDelegateSerializer
    {
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
