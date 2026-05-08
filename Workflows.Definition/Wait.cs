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
        public object ExplicitState { get; internal set; }
        internal Func<ValueTask> CancelAction { get; set; }

        public WorkflowContainer WorkflowContainer { get; set; }

        public Wait WithState<TState>(TState state)
        {
            ExplicitState = state;
            return this;
        }

        public Wait OnCanceled(Func<ValueTask> cancelAction)
        {
            CancelAction = cancelAction;
            return this;
        }

        public Wait OnCanceled<TState>(Func<TState, ValueTask> cancelAction)
        {
            CancelAction = new StatefulCancelActionInvoker<TState>(this, cancelAction).Invoke;
            return this;
        }

        private sealed class StatefulCancelActionInvoker<TState>
        {
            private readonly Wait _wait;
            private readonly Func<TState, ValueTask> _action;

            public StatefulCancelActionInvoker(Wait wait, Func<TState, ValueTask> action)
            {
                _wait = wait;
                _action = action;
            }

            public ValueTask Invoke()
            {
                return _action((TState)_wait.ExplicitState);
            }
        }
    }
}
