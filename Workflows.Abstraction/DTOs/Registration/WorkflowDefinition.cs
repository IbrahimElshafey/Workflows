using System;
using System.Collections.Generic;

namespace Workflows.Abstraction.DTOs.Registration
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
        public string WorkflowTypeSchema { get; set; }

        /// <summary>
        /// The UTC timestamp when this version was registered.
        /// </summary>
        public DateTime RegisteredAt { get; set; }
    }
}