using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Workflows.Primitives;

namespace Workflows.Abstraction.DTOs.Waits
{
    /// <summary>
    /// DTO for massive fan-out waits (WaitMany / WaitAny).
    /// Child wait metadata is persisted in a dedicated relational table,
    /// not inside the JSON state blob, to avoid state bloat.
    /// </summary>
    public class ExternalGroupWaitDto : WaitInfrastructureDto
    {
        /// <summary>
        /// Number of child waits in the fan-out.
        /// </summary>
        public int ChildCount { get; set; }

        /// <summary>
        /// Number of child waits that must complete for this wait to be considered matched.
        /// For WaitAny this is 1; for WaitMany this equals ChildCount.
        /// </summary>
        public int RequiredCompletedCount { get; set; }

        /// <summary>
        /// Optional child waits kept in memory for active execution.
        /// These are NOT serialized into the state blob; they are hydrated from the external table.
        /// </summary>
        [JsonIgnore]
        public List<WaitInfrastructureDto> ExternalChildWaits { get; set; } = new();

        /// <summary>
        /// True if this wait stores its children externally.
        /// </summary>
        [JsonIgnore]
        public bool IsExternal => WaitType == WaitType.WaitMany || WaitType == WaitType.WaitAny;
    }
}
