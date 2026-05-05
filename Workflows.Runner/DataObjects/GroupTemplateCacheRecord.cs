using System;
using System.Threading.Tasks;

namespace Workflows.Runner.DataObjects
{
    internal class GroupTemplateCacheRecord
    {
        // Callback: (workflowInstance, closure) => bool
        public Func<object, object, bool> GroupMatchFilter { get; set; }
        // Callback: (workflowInstance, closure) => ValueTask
        public Func<object, object, ValueTask> CancelAction { get; set; }
    }
}
