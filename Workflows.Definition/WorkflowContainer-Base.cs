using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Workflows.Definition
{
    public abstract partial class WorkflowContainer
    {
        public virtual async IAsyncEnumerable<Wait> Run()
        {
            yield break;
        }

        public virtual async IAsyncEnumerable<Wait> Run<T>(T state)
        {
            yield break;
        }

        internal Dictionary<Guid, object> WaitsStates { get; set; } = new Dictionary<Guid, object>();
        internal HashSet<string> TokensToCancel { get; set; } = new HashSet<string>();

        protected void CancelToken(string token)
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                TokensToCancel.Add(token);
            }
        }

        public virtual Task OnError(string message, Exception ex = null)
        {
            return Task.CompletedTask;
        }
        public virtual Task OnCompleted()
        {
            return Task.CompletedTask;
        }

        public virtual Type GetStateType() => typeof(DefaultWorkflowState);
        public virtual object GetState() => _defaultState;
        public virtual void SetState(object state)
        {
            if (state is DefaultWorkflowState ds)
            {
                _defaultState = ds;
            }
        }
        private DefaultWorkflowState _defaultState = new DefaultWorkflowState();
    }

    public abstract partial class WorkflowContainer<TState> : WorkflowContainer where TState : new()
    {
        public TState State { get; set; } = new TState();
        public TState state => State;

        public override IAsyncEnumerable<Wait> Run()
        {
            return Run(State);
        }

        public virtual IAsyncEnumerable<Wait> Run(TState state)
        {
            return base.Run(state);
        }

        public override Type GetStateType() => typeof(TState);
        public override object GetState() => State;
        public override void SetState(object stateObj)
        {
            if (stateObj is TState s)
            {
                State = s;
            }
            else if (stateObj != null && stateObj.GetType().FullName == "Newtonsoft.Json.Linq.JObject")
            {
                var method = stateObj.GetType().GetMethod("ToObject", new[] { typeof(Type) });
                if (method != null)
                {
                    var result = method.Invoke(stateObj, new object[] { typeof(TState) });
                    if (result is TState resolvedState)
                    {
                        State = resolvedState;
                    }
                }
            }
        }
    }

    public class DefaultWorkflowState
    {
    }
}