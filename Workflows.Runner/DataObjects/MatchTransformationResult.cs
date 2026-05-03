using System.Collections.Generic;
using System.Linq.Expressions;

namespace Workflows.Runner.DataObjects
{
    internal class MatchTransformationResult
    {
        /// <summary>
        /// Original match expression after rewtite to include the captured variables from the closure. 
        /// This is the expression that will be compiled and executed against incoming signals.
        /// It will be look like Expression<Func<TSignalData, TInstance, TClosure, bool>>
        /// </summary>
        public LambdaExpression MatchExpression { get; set; }
        /// <summary>
        /// Match expression rewritten against generic object like JObject
        /// If it can be genrated (no method calls and all are POCOs), it will be used for pre-filtering incoming signals before deserialization.
        /// It will be look like Expression<Func<TSignalDataGeneric, TInstanceGeneric, TClosureGeneric, bool>>
        /// </summary>
        public Expression GenericMatchExpression { get; set; }
        /// <summary>
        /// If the generic match expression covers the full match logic
        /// (i.e., it can be used as a standalone filter without needing to execute the original match expression).
        /// </summary>
        public bool IsGenericMatchFullMatch { get; internal set; }

        /// <summary>
        /// The InstanceExactMatchExpression is a lambda that produces an object[] of the mandatory part values from the original match expression.
        /// It takes input parameters (workflowInstance, closure) and evaluates the constant parts of the original match expression.
        /// It will look like Expression<Func<workflowInstance, closure, object[]>> with body: new object[] { 42, "Paid|Urgent", 12 }
        /// </summary>
        public LambdaExpression InstanceExactMatchExpression { get; internal set; }
        /// <summary>
        /// Paths to the properties in the signal data that are used for exact matching.
        /// This is used to correlate incoming signals to waiting points without needing to evaluate the full match expression.
        /// </summary>
        public List<string> SignalExactMatchPaths { get; internal set; } = new List<string>();
        /// <summary>
        /// The value returned from calling InstanceExactMatchExpression for current wait. 
        /// This is the pre-evaluated exact match value that can be used for quick correlation of incoming signals when use SignalExactMatchPaths.
        /// </summary>
        public string ExactMatchPart { get; internal set; }
        /// <summary>
        /// Gets a value indicating whether the match is both exact and a full match.
        /// </summary>
        public bool IsExactMatchFullMatch { get; internal set; }
        /// <summary>
        /// The closure object captured at the wait point.
        /// </summary>
        public object Closure { get; internal set; }
    }
}
