using System.Collections.Generic;

namespace Workflows.Abstraction.DTOs.Waits
{
    /// <summary>
    /// DTO for SignalWait that stores only instance-specific signal data.
    /// Template-level data (expressions, match paths, flags) is stored separately
    /// in the template cache and referenced via TemplateHashKey.
    /// Inherits from WaitInfrastructureDto to maintain compatibility with persistence infrastructure.
    /// </summary>
    public class SignalWaitDto : WaitInfrastructureDto
    {
        /// <summary>
        /// Unique identifier for the signal being awaited.
        /// </summary>
        public string SignalIdentifier { get; set; }

        /// <summary>
        /// Hash key referencing the match expression template in the template cache.
        /// Used to look up expressions and exact-match metadata without
        /// re-serializing them on every wait instance.
        /// </summary>
        public string? TemplateHashKey { get; set; }

        /// <summary>
        /// Instance-specific exact match part — the evaluated key/value pairs extracted
        /// from the current workflow state at mapping time. Used for fast DB-level filtering.
        /// </summary>
        public string ExactMatchPart { get; internal set; }

        /// <summary>
        /// Serialized callback to execute after successful match.
        /// Instance-specific because the delegate captures per-instance closure state.
        /// </summary>
        public string AfterMatchAction { get; set; }

        /// <summary>
        /// Serialized callback to execute if this wait is cancelled.
        /// Instance-specific because the delegate captures per-instance closure state.
        /// </summary>
        public string CancelAction { get; set; }

        /// <summary>
        /// Indicates if this is a first wait used for auto-instantiation.
        /// </summary>
        public bool IsFirstWait { get; set; }
    }
}
