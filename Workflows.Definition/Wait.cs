using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Workflows.Definition
{
    /// <summary>
    /// Base class for all wait types in the workflow engine.
    /// </summary>
    public class Wait
    {
        internal Wait(WaitType waitType, string waitName, int inCodeLine, string callerName)
        {
            Id = Guid.NewGuid();
            WaitType = waitType;
            WaitName = waitName;
            InCodeLine = inCodeLine;
            CallerName = callerName;
            Created = DateTime.UtcNow;
        }

        internal Guid Id { get; set; }
        internal string WaitName { get; set; }
        internal WaitType WaitType { get; set; }
        internal string CallerName { get; set; }
        internal int InCodeLine { get; set; }
        internal DateTime Created { get; set; }

        internal WaitStatus Status { get; set; } = WaitStatus.Waiting;
        internal int StateAfterWait { get; set; }
        internal string Path { get; set; }
        internal Guid? ParentWaitId { get; set; }
        internal Guid RequestedByWorkflowId { get; set; }
        internal Guid RootWorkflowId { get; set; }
        internal Guid WorkflowStateId { get; set; }
        internal List<Wait> ChildWaits { get; set; } = new();

        internal object LocalsValue { get; set; }
        internal string LocalsTypeName { get; set; }
        internal DateTime? LocalsCreated { get; set; }

        internal object ClosureValue { get; set; }
        internal string ClosureTypeName { get; set; }
        internal DateTime? ClosureCreated { get; set; }

        internal Func<ValueTask> CancelAction { get; set; }

        public Definition.WorkflowContainer CurrentWorkflow { get; set; }

        internal string ValidateCallback(Delegate callback, string methodName)
        {
            return null;
        }

        public Wait OnCanceled(Func<ValueTask> cancelAction)
        {
            CancelAction = cancelAction;
            return this;
        }

        internal void SetClosureObject(object closure)
        {
        }
    }
}
