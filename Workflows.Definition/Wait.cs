using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        internal Wait()
        {
        }

        internal Guid Id { get; set; }

        internal HashSet<string> CancelTokens { get; set; } = new HashSet<string>();
        internal string WaitName { get; set; }

        internal WaitType WaitType { get; set; }

        internal string CallerFilePath { get; private set; }

        internal string CallerName { get; set; }

        internal int InCodeLine { get; set; }

        internal DateTime Created { get; set; }

        internal int StateAfterWait { get; set; }

        internal List<Wait> ChildWaits { get; set; } = new();

        internal Guid StateKey { get; set; }
        internal object ExplicitState => 
            StateKey != Guid.Empty && WorkflowContainer?.WaitsStates != null && WorkflowContainer.WaitsStates.TryGetValue(StateKey, out var val) 
                ? val 
                : null;
        internal Delegate CancelAction { get; set; }
        internal string? CancelActionKey { get; set; }

        public WorkflowContainer WorkflowContainer { get; set; }

        public Wait WithState<TState>(TState state)
        {
            SetState(state);
            return this;
        }

        public Wait OnCanceled(
            Func<ValueTask> cancelAction,
            [CallerMemberName] string callerName = "",
            [CallerArgumentExpression(nameof(cancelAction))] string? expression = default)
        {
            CancelAction = cancelAction;
            CancelActionKey = Helpers.WorkflowHashCalculator.CalculateHash(expression, callerName, "Cancel_" + (WaitName ?? string.Empty));
            return this;
        }

        public Wait OnCanceled<TState>(
            Func<TState, ValueTask> cancelAction,
            [CallerMemberName] string callerName = "",
            [CallerArgumentExpression(nameof(cancelAction))] string? expression = default)
        {
            var invoker = new StatefulCancelActionInvoker<TState>(this, cancelAction);
            CancelAction = (Func<ValueTask>)invoker.Invoke;
            CancelActionKey = Helpers.WorkflowHashCalculator.CalculateHash(expression, callerName, "Cancel_" + (WaitName ?? string.Empty));
            return this;
        }

        internal void SetState(object state)
        {
            if (state == null) return;
            foreach (var kvp in WorkflowContainer.WaitsStates)
            {
                if (Equals(kvp.Value, state))
                {
                    StateKey = kvp.Key;
                    return;
                }
            }
            StateKey = Guid.NewGuid();
            WorkflowContainer.WaitsStates[StateKey] = state;
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
