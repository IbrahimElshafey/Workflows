using System;
using System.Threading.Tasks;

namespace Workflows.Runner.DataObjects
{
    internal class ExpressionTemplateCacheRecord
    {
        // Tier 3 Execution: (signalData, workflowInstance, closure) => bool
        public Func<object, object, object, bool> CompiledMatchDelegate { get; set; }

        // Callback: (workflowInstance, signalData, closure) => void
        public Action<object, object, object> AfterMatchAction { get; set; }

        // Callback: (workflowInstance, closure) => ValueTask
        public Func<object, object, ValueTask> CancelAction { get; set; }

        // Tier 1 Blueprint: (workflowInstance, closure) => object[]
        public Func<object, object, object[]> CompiledInstanceExactMatchExpression { get; set; }
    }
}
