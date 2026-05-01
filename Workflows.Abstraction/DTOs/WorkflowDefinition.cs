using System;
using System.Collections.Generic;

namespace Workflows.Abstraction.DTOs
{
    public class WorkflowDefinition
    {
        /// <summary>
        /// The unique name of the workflow (e.g., "OrderProcessing").
        /// </summary>
        public string WorkflowName { get; set; }

        /// <summary>
        /// The specific version string (e.g., "1.2.0"). 
        /// Ensures instances run on the code they were started with.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// The .NET type name used by the Runner to instantiate the WorkflowContainer.
        /// </summary>
        public string WorkflowTypeName { get; set; }

        /// <summary>
        /// List of Signal Paths that are marked as Start Nodes for this workflow.
        /// </summary>
        public List<string> StartNodeSignalPaths { get; set; } = new();

        /// <summary>
        /// Optional: Grouping or Category for management UI purposes.
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// The UTC timestamp when this version was registered.
        /// </summary>
        public DateTime RegisteredAt { get; set; }
    }
}