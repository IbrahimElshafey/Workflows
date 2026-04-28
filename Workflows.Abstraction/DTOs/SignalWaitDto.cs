using System;
using System.Collections.Generic;

namespace Workflows.Abstraction.DTOs
{
    public class SignalWaitDto : WaitBaseDto
    {
        public string MatchExpression { get; set; }
        /// <summary>
        /// Match expression rewritten agaist generic object like JObject or 
        /// </summary>
        public string GenericMatchExpression { get; set; }
        public bool IsGenericMatchFullMatch { get; internal set; }
        public string AfterMatchAction { get; set; }
        public string CancelAction { get; set; }
        public string SignalIdentifier { get; set; }

        public string ExactMatchPart { get; internal set; }
        public bool IsExactMatchFullMatch { get; internal set; }
        public List<string> SignalExactMatchPaths { get; internal set; }
    }
}