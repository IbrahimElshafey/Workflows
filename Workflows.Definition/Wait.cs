using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Primitives;

namespace Workflows.Definition
{
    /// <summary>
    /// Base class for all wait types in the workflow engine.
    /// </summary>
    public abstract class Wait
    {
        internal Wait(WaitType waitType, string waitName, int inCodeLine, string callerName, string callerFilePath)
        {
            Id = Guid.NewGuid();
            WaitType = waitType;
            WaitName = waitName;
            InCodeLine = inCodeLine;
            CallerName = callerName;
            Created = DateTime.UtcNow;
            CallerFilePath = callerFilePath;
        }

        internal Guid Id { get; set; }

        internal string WaitName { get; set; }

        internal WaitType WaitType { get; set; }

        internal string CallerFilePath { get; private set; }

        internal string CallerName { get; set; }

        internal int InCodeLine { get; set; }

        internal DateTime Created { get; set; }

        internal int StateAfterWait { get; set; }

        internal List<Wait> ChildWaits { get; set; } = new();

        public string ClosureKey { get; set; }
        internal Func<ValueTask> CancelAction { get; set; }

        public WorkflowContainer WorkflowContainer { get; set; }

        public Wait OnCanceled(Func<ValueTask> cancelAction)
        {
            CancelAction = cancelAction;
            return this;
        }
    }
}
