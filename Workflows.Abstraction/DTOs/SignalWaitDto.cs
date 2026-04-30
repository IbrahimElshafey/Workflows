using System.Collections.Generic;

namespace Workflows.Abstraction.DTOs
{
    /// <summary>
    /// DTO for SignalWait that stores signal matching and callback configuration.
    /// Inherits from WaitInfrastructureDto to maintain compatibility with persistence infrastructure.
    /// </summary>
    public class SignalWaitDto : WaitInfrastructureDto
    {
        /// <summary>
        /// Serialized match expression for filtering incoming signals.
        /// </summary>
        public string MatchExpression { get; set; }
        
        /// <summary>
        /// Hash of the match expression for optimization and deduplication.
        /// </summary>
        public object MatchExpressionHash { get; set; }
        
        /// <summary>
        /// Match expression rewritten against generic object (e.g., JObject).
        /// </summary>
        public string GenericMatchExpression { get; set; }
        
        /// <summary>
        /// Whether the generic match expression covers the full match.
        /// </summary>
        public bool IsGenericMatchFullMatch { get; internal set; }
        
        /// <summary>
        /// Serialized callback to execute after successful match.
        /// </summary>
        public string AfterMatchAction { get; set; }
        
        /// <summary>
        /// Serialized callback to execute if this wait is cancelled.
        /// </summary>
        public string CancelAction { get; set; }
        
        /// <summary>
        /// Unique identifier for the signal being awaited.
        /// </summary>
        public string SignalIdentifier { get; set; }

        /// <summary>
        /// Exact match part of the filter expression.
        /// </summary>
        public string ExactMatchPart { get; internal set; }
        
        /// <summary>
        /// Whether the exact match covers the full match.
        /// </summary>
        public bool IsExactMatchFullMatch { get; internal set; }
        
        /// <summary>
        /// Paths used for exact matching against signal properties.
        /// </summary>
        public List<string> SignalExactMatchPaths { get; internal set; }
    }
}