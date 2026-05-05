using System;
using System.Threading.Tasks;

namespace Workflows.Runner.Cache
{
    /// <summary>
    /// The key for this record will be a hash that calcualted based on (MatchExpressionTextForm, CancelActionName, AfterMatchAction).
    /// This allows us to cache the compiled expressions for each unique wait in the workflow.
    /// </summary>
    internal class SignalTemplateCacheRecord
    {
        /// <summary>
        /// Wait.MatchExpression: (workflowInstance, signalData, closure) => bool
        /// </summary>
        public Func<object, object, object, bool> CompiledMatchDelegate { get; set; }

        /// <summary>
        /// Wait.AfterMatchAction (workflowInstance, signalData, closure) => void
        /// </summary>
        public Action<object, object, object> AfterMatchAction { get; set; }

        /// <summary>
        /// Wait.CancelAction (workflowInstance, closure) => ValueTask
        /// </summary>
        public Func<object, object, ValueTask> CancelAction { get; set; }

        // Tier 1 Blueprint: (workflowInstance, closure) => object[]
        public Func<object, object, string> CompiledInstanceExactMatchExpression { get; set; }
    }
}
