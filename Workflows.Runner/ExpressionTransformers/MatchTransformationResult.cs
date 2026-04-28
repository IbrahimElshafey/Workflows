using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class MatchTransformationResult
    {
        public Expression MatchExpression { get; set; }
        /// <summary>
        /// Match expression rewritten agaist generic object like JObject or 
        /// </summary>
        public Expression GenericMatchExpression { get; set; }
        public bool IsGenericMatchFullMatch { get; internal set; }
        public string ExactMatchPart { get; internal set; }
        public bool IsExactMatchFullMatch { get; internal set; }
        public List<string> SignalExactMatchPaths { get; internal set; }
    }
}
