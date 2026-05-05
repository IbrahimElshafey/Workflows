using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Workflows.Definition
{
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

        string ISignalWait.SignalIdentifier => SignalIdentifier;
        object ISignalWait.AfterMatchAction => AfterMatchAction;

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
    }
}