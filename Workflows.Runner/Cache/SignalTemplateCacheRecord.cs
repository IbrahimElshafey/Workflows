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
        /// Wait.MatchExpression: (workflowInstance, signalData, state) => bool
        /// </summary>
        public Func<object, object, object, bool> CompiledMatchDelegate { get; set; }

        /// <summary>
        /// Wait.AfterMatchAction (workflowInstance, signalData, state) => void
        /// </summary>
        public Action<object, object, object> AfterMatchAction { get; set; }

        /// <summary>
        /// Wait.CancelAction (workflowInstance, state) => ValueTask
        /// </summary>
        public Func<object, object, ValueTask> CancelAction { get; set; }

        // Tier 1 Blueprint: (workflowInstance, state) => object[]
        public Func<object, object, string[]> CompiledInstanceExactMatchExpression { get; set; }
    }
}
