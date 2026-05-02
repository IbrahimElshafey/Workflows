using System;

namespace Workflows.Definition.Data.DTOs
{
    /// <summary>
    /// Core wait configuration DTO containing only essential wait definition properties.
    /// Lightweight and focused on workflow definition, not persistence infrastructure.
    /// </summary>
    public abstract class WaitCoreDto
    {
        /// <summary>
        /// The name/identifier of this wait.
        /// </summary>
        public string WaitName { get; internal set; }

        /// <summary>
        /// The type of wait (Signal, Time, Command, etc.).
        /// </summary>
        public Enums.WaitType WaitType { get; internal set; }

        /// <summary>
        /// Name of the method that created this wait.
        /// </summary>
        public string CallerName { get; internal set; }

        /// <summary>
        /// Line number in the source code where this wait was created.
        /// </summary>
        public int InCodeLine { get; internal set; }

        /// <summary>
        /// UTC timestamp when this wait was created.
        /// </summary>
        public DateTime Created { get; internal set; }
    }
}