using Workflows.Handler.InOuts.Entities;
using System.Linq.Expressions;

using System;
namespace Workflows.Handler.BaseUse
{
    public class MethodWait<TInput, TOutput> : Wait
    {
        //todo: add MatchException
        internal MethodWaitEntity<TInput, TOutput> MethodWaitEntity { get; }

        internal MethodWait(MethodWaitEntity<TInput, TOutput> wait) : base(wait)
        {
            MethodWaitEntity = wait;
        }

        public MethodWait<TInput, TOutput> AfterMatch(Action<TInput, TOutput> afterMatchAction)
        {
            MethodWaitEntity.AfterMatch(afterMatchAction);
            return this;
        }

        public MethodWait<TInput, TOutput> MatchIf(bool condition, Expression<Func<TInput, TOutput, bool>> matchExpression)
        {
            if (condition)
                MethodWaitEntity.MatchIf(matchExpression);
            return this;
        }

        public MethodWait<TInput, TOutput> MatchIf(Expression<Func<TInput, TOutput, bool>> matchExpression)
        {
            MethodWaitEntity.MatchIf(matchExpression);
            return this;
        }

        /// <summary>
        /// Callback execusted when the wait canceled because it's a part of wait group that one match is sufficent
        /// </summary>
        /// <param name="cancelAction">Action to execute when cancel</param>
        public MethodWait<TInput, TOutput> WhenCancel(Action cancelAction)
        {
            MethodWaitEntity.WhenCancel(cancelAction);
            return this;
        }

        public MethodWait<TInput, TOutput> MatchAny(bool condition = true)
        {
            if (condition)
                MethodWaitEntity.MatchAny();
            return this;
        }

        public MethodWait<TInput, TOutput> NoActionAfterMatch()
        {
            MethodWaitEntity.NoActionAfterMatch();
            return this;
        }

    }
}