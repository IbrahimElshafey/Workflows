using System;
using System.Collections.Concurrent;

namespace Workflows.Runner
{
    internal class TypesCache
    {
        private readonly ConcurrentDictionary<string, Type> _cache = new(StringComparer.Ordinal);

        public Type GetOrAdd(string workflowTypeName, Func<string, Type> factory)
        {
            return _cache.GetOrAdd(workflowTypeName, factory);
        }
    }
}
