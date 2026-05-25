using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.Json;

namespace Workflows.Runner.DataObjects
{
    /// <summary>
    /// ⚠️ ARCHITECTURAL WARNING: 
    /// This class holds heavy Roslyn Expression Trees. It is designed to be an 
    /// EPHEMERAL object used by the Runner to compile lightweight native delegates. 
    /// DO NOT store instances of this class in long-lived memory caches (ConcurrentDictionary), 
    /// as it will cause massive memory leaks.
    /// </summary>
    internal class MatchTransformationResult
    {
        /// <summary>
        /// Original match expression after rewrite to include the caller _this WorkflowInstance.
        /// This is the expression that will be compiled and executed against incoming signals.
        /// It will look like Expression<Func<TSignalData, TInstance, TState, bool>>
        /// </summary>
        public Expression<Func<object,object,object,bool>> MatchExpression { get; init; }

        /// <summary>
        /// Match expression rewritten against generic object like JsonElement.
        /// If it can be generated (no method calls and all are POCOs), it will be used for 
        /// pre-filtering incoming signals at Tier 1.5 before waking up the Runners.
        /// It will look like Expression (Func(TSignalData, TState, TInstance, bool))
        /// </summary>
        public Expression<Func<JsonElement, JsonElement, JsonElement, bool>> GenericMatchExpression { get; init; }

        /// <summary>
        /// Indicates if the generic match expression covers the full match logic
        /// (i.e., it can be used as a standalone filter without ever needing to execute the original match expression).
        /// </summary>
        public bool IsGenericMatchFullMatch { get; init; }

        /// <summary>
        /// The lambda that produces an array of the mandatory exact match values from current instance and state.
        /// It takes input parameters (workflowInstance, State) and evaluates the constant parts.
        /// Example: Expression<Func<workflowInstance, State, string[]>> with body: new string[] { "42", "Paid" }
        /// </summary>
        public Expression<Func<object,object,string[]>> InstanceExactMatchExpression { get; init; }

        /// <summary>
        /// Paths to the properties in the signal data that are used for exact matching.
        /// This is used to correlate incoming signals to waiting points without needing to evaluate the full match expression.
        /// </summary>
        public List<string> SignalExactMatchPaths { get; init; } = new List<string>();

        /// <summary>
        /// The array of evaluated string values returned from compiling and invoking the InstanceExactMatchExpression.
        /// These are the pre-evaluated exact match keys (e.g., ["42", "Paid"]) ready to be saved as SQL routing indexes.
        /// </summary>
        public string[] ExactMatchParts { get; init; }

        /// <summary>
        /// Gets a value indicating whether the match is both exact and a full match.
        /// </summary>
        public bool IsExactMatchFullMatch { get; init; }
    }
}