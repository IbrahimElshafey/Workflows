using System.Collections.Generic;
using System.Linq.Expressions;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class MatchTransformationResult
    {
        public LambdaExpression MatchExpression { get; set; }
        /// <summary>
        /// Match expression rewritten against generic object like JObject
        /// </summary>
        public Expression GenericMatchExpression { get; set; }
        public bool IsGenericMatchFullMatch { get; internal set; }
        public string ExactMatchPart { get; internal set; }
        public bool IsExactMatchFullMatch { get; internal set; }
        // FIX: Initialize the list to prevent NullReferenceExceptions downstream
        public List<string> SignalExactMatchPaths { get; internal set; } = new List<string>();
        public LambdaExpression InstanceExactMatchExpression { get; internal set; }
        public object Closure { get; internal set; }
    }
}
