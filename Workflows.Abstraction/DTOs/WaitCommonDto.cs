using System.Collections.Generic;

using System;
using Workflows.Abstraction.Enums;

namespace Workflows.Abstraction.DTOs
{
    public abstract class WaitBaseDto
    {
        public List<WaitBaseDto> ChildWaits { get; set; } = new();

        public Guid? ParentWaitId { get; set; }

        public Guid RequestedByWorkflowId { get; set; }

        public Guid WorkflowStateId { get; set; }

        public string CallerName { get; internal set; }

        public PrivateData ClosureData { get; internal set; }

        public DateTime Created { get; internal set; }

        public Guid Id { get; internal set; } = new Guid();
        public PresistStatus PresistStatus {  get; internal set; }


        public int InCodeLine { get; internal set; }

        public bool IsFirst { get; internal set; }

        public bool IsRoot { get; internal set; }

        /// <summary>
        /// Local variables in method at the wait point where current wait requested It's the runner class serialized we
        /// can rename this to RunnerState
        /// </summary>
        public PrivateData Locals { get; internal set; }

        public DateTime Modified { get; internal set; }


        public string Path { get; internal set; }

        public Guid RootWorkflowId { get; internal set; }

        public int StateAfterWait { get; internal set; }

        public WaitStatus Status { get; internal set; } = WaitStatus.Waiting;

        public string WaitName { get; internal set; }

        public WaitType WaitType { get; internal set; }

    }
}