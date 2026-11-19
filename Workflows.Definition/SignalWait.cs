using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition.Helpers;
using Workflows.Primitives;

namespace Workflows.Definition
{
    public readonly struct SignalBuilder<TSignal>
    {
        private readonly SignalWait<TSignal> _wait;

        internal SignalBuilder(SignalWait<TSignal> wait) => _wait = wait;

        public SignalBuilder<TSignal> WithCancelToken(string token)
        {
            _wait.WithCancelToken(token);
            return this;
        }

        public SignalBuilder<TSignal> OnCanceled(
            Func<ValueTask> cancelAction,
            [CallerMemberName] string callerName = "",
            [CallerArgumentExpression(nameof(cancelAction))] string? expression = default)
        {
            _wait.OnCanceled(cancelAction, callerName, expression);
            return this;
        }

        public SignalBuilder<TSignal> AfterMatch(Action<TSignal> afterMatchAction)
        {
            _wait.AfterMatch(afterMatchAction);
            return this;
        }

        public SignalBuilder<TSignal> MatchAny()
        {
            _wait.MatchAny();
            return this;
        }

        public SignalBuilder<TSignal> MatchIf(
            Expression<Func<TSignal, bool>> matchExpression,
            [CallerMemberName] string callerName = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            _wait.MatchIf(matchExpression, callerName, callerLineNumber, expression);
            return this;
        }

        public StatefulSignalBuilder<TSignal, TState> WithState<TState>(TState state)
        {
            _wait.SetState(state);
            return new StatefulSignalBuilder<TSignal, TState>(_wait);
        }

        public static implicit operator SignalWait<TSignal>(SignalBuilder<TSignal> builder) => builder._wait;
        public static implicit operator Wait(SignalBuilder<TSignal> builder) => builder._wait;
        public static implicit operator WaitInfrastructureDto(SignalBuilder<TSignal> builder) => WaitDtoConversion.Convert(builder._wait);
    }

    public readonly struct StatefulSignalBuilder<TSignal, TState>
    {
        private readonly SignalWait<TSignal> _wait;

        internal StatefulSignalBuilder(SignalWait<TSignal> wait) => _wait = wait;

        public StatefulSignalBuilder<TSignal, TState> WithCancelToken(string token)
        {
            _wait.WithCancelToken(token);
            return this;
        }

        public StatefulSignalBuilder<TSignal, TState> OnCanceled(
            Func<TState, ValueTask> cancelAction,
            [CallerMemberName] string callerName = "",
            [CallerArgumentExpression(nameof(cancelAction))] string? expression = default)
        {
            _wait.OnCanceled(cancelAction, callerName, expression);
            return this;
        }

        public StatefulSignalBuilder<TSignal, TState> OnCanceled(
            Func<ValueTask> cancelAction,
            [CallerMemberName] string callerName = "",
            [CallerArgumentExpression(nameof(cancelAction))] string? expression = default)
        {
            _wait.OnCanceled(cancelAction, callerName, expression);
            return this;
        }

        public StatefulSignalBuilder<TSignal, TState> AfterMatch(Action<TSignal, TState> afterMatchAction)
        {
            _wait.AfterMatch(afterMatchAction);
            return this;
        }

        public StatefulSignalBuilder<TSignal, TState> AfterMatch(Action<TSignal> afterMatchAction)
        {
            _wait.AfterMatch(afterMatchAction);
            return this;
        }

        public StatefulSignalBuilder<TSignal, TState> MatchAny()
        {
            _wait.MatchAny();
            return this;
        }

        public StatefulSignalBuilder<TSignal, TState> MatchIf(
            Expression<Func<TSignal, TState, bool>> matchExpression,
            [CallerMemberName] string callerName = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            _wait.MatchIf(matchExpression, callerName, callerLineNumber, expression);
            return this;
        }

        public StatefulSignalBuilder<TSignal, TState> MatchIf(
            Expression<Func<TSignal, bool>> matchExpression,
            [CallerMemberName] string callerName = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            _wait.MatchIf(matchExpression, callerName, callerLineNumber, expression);
            return this;
        }

        public static implicit operator SignalWait<TSignal>(StatefulSignalBuilder<TSignal, TState> builder) => builder._wait;
        public static implicit operator Wait(StatefulSignalBuilder<TSignal, TState> builder) => builder._wait;
        public static implicit operator WaitInfrastructureDto(StatefulSignalBuilder<TSignal, TState> builder) => WaitDtoConversion.Convert(builder._wait);
    }

    /// <summary>
    /// Represents a passive wait for an external signal event. Signals do not initiate side effects, so they can be
    /// safely combined with other passive waits in group scenarios.
    /// </summary>
    public partial class SignalWait<SignalData> : Wait, ISignalWait
    {
        internal Delegate AfterMatchAction { get; set; }

        /// <summary>
        /// Stable hash key computed from the expression text, caller method name, and signal
        /// identifier. Stored in the DTO so the runner can look up the live delegate in
        /// <see cref="ICallbackRegistry"/> without reflection.
        /// </summary>
        internal string? HandlerKey { get; set; }

        internal SignalWait(
            string signalIdentifier,
            string waitName,
            int inCodeLine,
            string callerName,
            string callerFilepath) : base(WaitType.SignalWait, waitName, inCodeLine, callerName, callerFilepath)
        {
            SignalIdentifier = signalIdentifier;
        }

        internal SignalWait()
        {
        }

        internal LambdaExpression MatchExpression { get; set; }

        internal string MatchExpressionAsText { get; set; }

        internal string SignalIdentifier { get; set; }

        LambdaExpression ISignalWait.MatchExpression { get => MatchExpression; set => MatchExpression = value; }
        object ISignalWait.ExplicitState => ExplicitState;

        string ISignalWait.SignalIdentifier => SignalIdentifier;

        internal SignalWait<SignalData> WithState<TState>(TState state)
        {
            SetState(state);
            return this;
        }

        internal SignalWait<SignalData> AfterMatch<TState>(Action<SignalData, TState> afterMatchAction)
        {
            var invoker = new StatefulAfterMatchInvoker<TState>(this, afterMatchAction);
            AfterMatchAction = (Action<SignalData>)invoker.Invoke;
            return this;
        }

        internal SignalWait<SignalData> AfterMatch(Action<SignalData> afterMatchAction)
        {
            AfterMatchAction = afterMatchAction;
            return this;
        }

        internal SignalWait<SignalData> MatchAny()
        {
            MatchExpression = null;
            return this;
        }

        internal SignalWait<SignalData> MatchIf<TState>(
            Expression<Func<SignalData, TState, bool>> matchExpression,
            string callerName = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            MatchExpression = matchExpression;
            InCodeLine = callerLineNumber;
            MatchExpressionAsText = expression;
            HandlerKey = WorkflowHashCalculator.CalculateHash(expression, callerName, "Match_" + SignalIdentifier);
            return this;
        }

        internal SignalWait<SignalData> MatchIf(
            Expression<Func<SignalData, bool>> matchExpression,
            string callerName = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            MatchExpression = matchExpression;
            InCodeLine = callerLineNumber;
            MatchExpressionAsText = expression;
            HandlerKey = WorkflowHashCalculator.CalculateHash(expression, callerName, "Match_" + SignalIdentifier);
            return this;
        }

         

        internal SignalWait<SignalData> WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return this;
            CancelTokens.Add(token);
            return this;
        }

        private sealed class StatefulAfterMatchInvoker<TState>
        {
            private readonly SignalWait<SignalData> _wait;
            private readonly Action<SignalData, TState> _action;

            public StatefulAfterMatchInvoker(SignalWait<SignalData> wait, Action<SignalData, TState> action)
            {
                _wait = wait;
                _action = action;
            }

            public void Invoke(SignalData signalData)
            {
                _action(signalData, (TState)_wait.ExplicitState);
            }
        }
    }
}