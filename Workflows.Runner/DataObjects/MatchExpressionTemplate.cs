using System;
using System.Threading.Tasks;
namespace Workflows.Runner.DataObjects
{
    internal class MatchExpressionTemplate
    {
        // Tier 3 Execution: (signalData, workflowInstance, closure) => bool
        // Compiled bridge with internal casts for native speed
        public Func<object, object, object, bool> CompiledMatchDelegate { get; set; }

        // Callback: (workflowInstance, signalData, closure) => void
        // Bridge compiled once: handles sync/async and type-casting automatically
        public Func<object, object, object> AfterMatchAction { get; set; }

        // Callback: (workflowInstance, closure) => ValueTask
        public Func<object, object, ValueTask> CancelAction { get; set; }

        // Tier 1 Blueprint: (workflowInstance, closure) => object[]
        public Func<object, object, object[]> CompiledInstanceExactMatchExpression { get; set; }
    }
}