using System;

namespace Workflows.Abstraction.DTOs
{
    /// <summary>
    /// Represents a request to start a new workflow instance with optional input parameters.
    /// </summary>
    public class StartWorkflowRequest
    {
        /// <summary>
        /// The name of the workflow definition to start.
        /// </summary>
        public string WorkflowName { get; set; }

        /// <summary>
        /// Optional specific version to start. 0 means use the latest version.
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// Optional input payload to pass to the workflow container properties.
        /// </summary>
        public object Input { get; set; }
    }
}
