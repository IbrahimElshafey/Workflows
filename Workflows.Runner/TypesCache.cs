using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Definition;

namespace Workflows.Runner
{
    //Todo: when I call IWorkflowRegister.RegisterWorkflow<TWorkflow>(),
    // we also register a scoped workflow, so we can remove this class
    // and directly use the scoped workflow type for deserialization and execution,
    // which will save one level of indirection and also make it easier to support multiple versions of the same workflow.
    internal class TypesCache
    {
        private readonly ConcurrentDictionary<string, Type> _cache = new(StringComparer.Ordinal);

        public Type GetOrAdd(string workflowTypeName, Func<string, Type> factory)
        {
            return _cache.GetOrAdd(workflowTypeName, factory);
        }
    }
}
