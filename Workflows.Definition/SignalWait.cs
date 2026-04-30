using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Workflows.Abstraction.DTOs;

namespace Workflows.Handler.BaseUse
{
    public partial class SignalWait<SignalData> : Wait, ISignalWait
    {
        internal Action<SignalData> AfterMatchAction { get; set; }

        internal SignalWait(SignalWaitDto data) : base(data) { Data = data; }

        internal SignalWaitDto Data { get; private set; }


        internal LambdaExpression MatchExpression { get; set; }

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
            [CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            MatchExpression = matchExpression;
            Data.MatchExpressionHash = CalcMatchExpressionHash(callerFilePath,callerLineNumber,expression);
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

        /// <summary>
        /// Callback execusted when the wait canceled because it's a part of wait group that one match is sufficent
        /// </summary>
        /// <param name="cancelAction">Action to execute when cancel</param>
        public SignalWait<SignalData> WhenCancel(Action cancelAction)
        {
            CancelAction = cancelAction;
            return this;
        }
    }
}