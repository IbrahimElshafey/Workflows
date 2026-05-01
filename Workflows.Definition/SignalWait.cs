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
    public partial class SignalWait<SignalData> : Definition.Wait, Definition.IPassiveWait, ISignalWait
    {
        internal Action<SignalData> AfterMatchAction { get; set; }

        internal SignalWait(DTOs.SignalWaitDto data) : base(data) { Data = data; }

        internal DTOs.SignalWaitDto Data { get; private set; }


        internal LambdaExpression MatchExpression { get; set; }

        LambdaExpression ISignalWait.MatchExpression
        {
            get => MatchExpression;
            set => MatchExpression = value;
        }

        public Definition.SignalWait<SignalData> AfterMatch(Action<SignalData> afterMatchAction)
        {
            AfterMatchAction = afterMatchAction;
            return this;
        }

        public Definition.SignalWait<SignalData> MatchAny(bool condition = true)
        {
            if (condition)
            {
                MatchExpression = null;
            }
            return this;
        }

        public Definition.SignalWait<SignalData> MatchIf(
            Expression<Func<SignalData, bool>> matchExpression,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            MatchExpression = matchExpression;
            Data.MatchExpressionHash = CalcMatchExpressionHash(callerFilePath, callerLineNumber, expression);
            return this;
        }

        private object CalcMatchExpressionHash(string callerFilePath, int callerLineNumber, string? expression)
        {
            throw new NotImplementedException();
        }

        public Definition.SignalWait<SignalData> NoActionAfterMatch()
        {
            AfterMatchAction = null;
            return this;
        }

        /// <summary>
        /// Callback execusted when the wait canceled because it's a part of wait group that one match is sufficent
        /// </summary>
        /// <param name="cancelAction">Action to execute when cancel</param>
        public Definition.SignalWait<SignalData> WhenCancel(Action cancelAction)
        {
            CancelAction = cancelAction;
            return this;
        }

        public HashSet<string> CancelTokens { get; set; }

        public Definition.SignalWait<SignalData> WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens ??= new HashSet<string>();
            CancelTokens.Add(token);
            return this;
        }

        Definition.IPassiveWait Definition.IPassiveWait.WithCancelToken(string token) => WithCancelToken(token);
    }
}