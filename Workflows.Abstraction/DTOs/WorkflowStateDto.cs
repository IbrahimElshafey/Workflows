using System;
using System.Collections.Generic;
using Workflows.Abstraction.Enums;

namespace Workflows.Abstraction.DTOs
{
    public class WorkflowStateDto
    {
        public Guid Id { get; internal set; }
        public DateTime Created { get; internal set; }
        /// <summary>
        /// Serialized class instance that contain the resumable workflow instance data
        /// </summary>
        //todo: better to keep it string and same for PrivateData
        public object StateObject { get; internal set; }

        public List<WaitBaseDto> Waits { get; internal set; } = new();

        public WorkflowInstanceStatus Status { get; internal set; }
    }
}
