using System;
using System.Threading.Tasks;

namespace Workflows.Runner.Cache
{
    internal class CommandTemplateCacheRecord
    {
        // Callback: (workflowInstance, commandResult, closure) => void
        public Func<object, object, object> OnResultAction { get; set; }

        // Callback: (workflowInstance, commandResult, closure) => ValueTask
        public Func<object, object, object, ValueTask> CompensationAction { get; set; }

        // Callback: (workflowInstance, exception, closure) => ValueTask
        public Func<object, Exception, object, ValueTask> OnFailureAction { get; set; }
    }
}
