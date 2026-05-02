using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Workflows.Definition
{
    /// <summary>
    /// Represents a passive wait for an external signal event.
    /// Signals do not initiate side effects, so they can be safely combined
    /// with other passive waits in group scenarios.
    /// </summary>
    public partial class SignalWait<SignalData> : Wait, IPassiveWait, ISignalWait
    {
        internal Action<SignalData> AfterMatchAction { get; set; }

        internal SignalWait(string signalIdentifier, string waitName, int inCodeLine, string callerName)
            : base(WaitType.SignalWait, waitName, inCodeLine, callerName)
        {
            SignalIdentifier = signalIdentifier;
        }

        internal LambdaExpression MatchExpression { get; set; }

        internal object TemplateHashKey { get; set; }
        internal string MatchExpressionSerialized { get; set; }
        internal string GenericMatchExpressionSerialized { get; set; }
        internal bool IsGenericMatchFullMatch { get; set; }
        internal string AfterMatchActionSerialized { get; set; }
        internal string CancelActionSerialized { get; set; }
        internal string ExactMatchPartSerialized { get; set; }
        internal bool IsExactMatchFullMatch { get; set; }
        internal List<string> SignalExactMatchPaths { get; set; }
        internal string SignalIdentifier { get; set; }

        LambdaExpression ISignalWait.MatchExpression
        {
            get => MatchExpression;
            set => MatchExpression = value;
        }

        public SignalWait<SignalData> AfterMatch(Action<SignalData> afterMatchAction)
        {
            AfterMatchAction = afterMatchAction;
            return this;
        }

        public SignalWait<SignalData> MatchAny(bool condition = true)
        {
            if (condition)
            {
                MatchExpression = null;
            }
            return this;
        }

        public SignalWait<SignalData> MatchIf(
            Expression<Func<SignalData, bool>> matchExpression,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            MatchExpression = matchExpression;
            TemplateHashKey = CalcMatchExpressionHash(callerFilePath, callerLineNumber, expression);
            return this;
        }

        private object CalcMatchExpressionHash(string callerFilePath, int callerLineNumber, string? expression)
        {
            throw new NotImplementedException();
        }

        public SignalWait<SignalData> NoActionAfterMatch()
        {
            AfterMatchAction = null;
            return this;
        }

        public HashSet<string> CancelTokens { get; set; }

        public SignalWait<SignalData> WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens ??= new HashSet<string>();
            CancelTokens.Add(token);
            return this;
        }

        IPassiveWait IPassiveWait.WithCancelToken(string token) => WithCancelToken(token);
    }
}
