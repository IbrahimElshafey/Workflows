using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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

        public SignalBuilder<TSignal> OnCanceled(Func<ValueTask> cancelAction)
        {
            _wait.OnCanceled(cancelAction);
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
            [CallerLineNumber] int callerLineNumber = 0,
            [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            _wait.MatchIf(matchExpression, callerLineNumber, expression);
            return this;
        }

        public StatefulSignalBuilder<TSignal, TState> WithState<TState>(TState state)
        {
            _wait.ExplicitState = state;
            return new StatefulSignalBuilder<TSignal, TState>(_wait);
        }

        public SignalWait<TSignal> AsWait() => _wait;
        public IPassiveWait AsPassiveWait() => _wait;

        public static implicit operator SignalWait<TSignal>(SignalBuilder<TSignal> builder) => builder._wait;
        public static implicit operator Wait(SignalBuilder<TSignal> builder) => builder._wait;
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

        public StatefulSignalBuilder<TSignal, TState> OnCanceled(Func<TState, ValueTask> cancelAction)
        {
            _wait.OnCanceled(cancelAction);
            return this;
        }

        public StatefulSignalBuilder<TSignal, TState> OnCanceled(Func<ValueTask> cancelAction)
        {
            _wait.OnCanceled(cancelAction);
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
            [CallerLineNumber] int callerLineNumber = 0,
            [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            _wait.MatchIf(matchExpression, callerLineNumber, expression);
            return this;
        }

        public StatefulSignalBuilder<TSignal, TState> MatchIf(
            Expression<Func<TSignal, bool>> matchExpression,
            [CallerLineNumber] int callerLineNumber = 0,
            [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            _wait.MatchIf(matchExpression, callerLineNumber, expression);
            return this;
        }

        public SignalWait<TSignal> AsWait() => _wait;
        public IPassiveWait AsPassiveWait() => _wait;

        public static implicit operator SignalWait<TSignal>(StatefulSignalBuilder<TSignal, TState> builder) => builder._wait;
        public static implicit operator Wait(StatefulSignalBuilder<TSignal, TState> builder) => builder._wait;
    }

    /// <summary>
    /// Represents a passive wait for an external signal event. Signals do not initiate side effects, so they can be
    /// safely combined with other passive waits in group scenarios.
    /// </summary>
    public partial class SignalWait<SignalData> : Wait, IPassiveWait, ISignalWait
    {
        internal Action<SignalData> AfterMatchAction { get; set; }

        internal SignalWait(
            string signalIdentifier,
            string waitName,
            int inCodeLine,
            string callerName,
            string callerFilepath) : base(WaitType.SignalWait, waitName, inCodeLine, callerName, callerFilepath)
        {
            SignalIdentifier = signalIdentifier;
        }

        internal LambdaExpression MatchExpression { get; set; }

        internal string MatchExpressionAsText { get; set; }

        internal string SignalIdentifier { get; set; }

        LambdaExpression ISignalWait.MatchExpression { get => MatchExpression; set => MatchExpression = value; }
        object ISignalWait.ExplicitState => ExplicitState;

        string ISignalWait.SignalIdentifier => SignalIdentifier;
        object ISignalWait.AfterMatchAction => AfterMatchAction;

        public SignalWait<SignalData> WithState<TState>(TState state)
        {
            ExplicitState = state;
            return this;
        }

        public SignalWait<SignalData> AfterMatch<TState>(Action<SignalData, TState> afterMatchAction)
        {
            AfterMatchAction = new StatefulAfterMatchInvoker<TState>(this, afterMatchAction).Invoke;
            return this;
        }

        public SignalWait<SignalData> AfterMatch(Action<SignalData> afterMatchAction)
        {
            AfterMatchAction = afterMatchAction;
            return this;
        }

        public SignalWait<SignalData> MatchAny()
        {
            MatchExpression = null;
            return this;
        }

        public SignalWait<SignalData> MatchIf<TState>(
            Expression<Func<SignalData, TState, bool>> matchExpression,
            [CallerLineNumber] int callerLineNumber = 0,
            [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            MatchExpression = matchExpression;
            InCodeLine = callerLineNumber;
            MatchExpressionAsText = expression;
            return this;
        }

        public SignalWait<SignalData> MatchIf(
            Expression<Func<SignalData, bool>> matchExpression,
            [CallerLineNumber] int callerLineNumber = 0,
            [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            MatchExpression = matchExpression;
            InCodeLine = callerLineNumber;
            MatchExpressionAsText = expression;
            return this;
        }

        public HashSet<string> CancelTokens { get; set; }

        public SignalWait<SignalData> WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return this;
            CancelTokens ??= new HashSet<string>();
            CancelTokens.Add(token);
            return this;
        }

        IPassiveWait IPassiveWait.WithCancelToken(string token) => WithCancelToken(token);

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