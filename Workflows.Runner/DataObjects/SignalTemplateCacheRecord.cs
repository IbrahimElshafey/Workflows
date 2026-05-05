using System;
using System.Threading.Tasks;
using static FastExpressionCompiler.ExpressionCompiler;

namespace Workflows.Runner.DataObjects
{
    /// <summary>
    /// The key for this record will be a hash that calcualted based on (MatchExpressionTextForm, CancelActionName, AfterMatchAction).
    /// This allows us to cache the compiled expressions for each unique wait in the workflow.
    /// </summary>
    internal class SignalTemplateCacheRecord
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
