using System;

namespace Workflows.Abstraction.DTOs
{
    public class SignalDefinition
    {
        /// <summary>
        /// The unique path or name used to route the signal (e.g., "Payments.Completed").
        /// </summary>
        public string SignalIdentifier { get; set; }

        /// <summary>
        /// The .NET type name of the expected payload (TSignal).
        /// Used for JSON deserialization validation.
        /// </summary>
        public string PayloadTypeName { get; set; }
        public string PayloadSchema { get; set; }

        /// <summary>
        /// Description of what this signal represents for documentation/UI.
        /// </summary>
        public string Description { get; set; }
    }
}