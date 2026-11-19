using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Workflows.Primitives;

namespace Workflows.Definition
{
    /// <summary>
    /// Represents a composite group of passive waits that can be combined
    /// using MatchAll(), MatchAny(), or custom MatchIf() logic.
    /// </summary>
    public class GroupWait : Wait
    {

        internal GroupWait(string waitName, IReadOnlyList<Wait> childWaits, int inCodeLine, string callerName, string callerFilePath)
            : base(WaitType.GroupWaitAll, waitName, inCodeLine, callerName, callerFilePath)
        {
            ChildWaits = childWaits?.ToList() ?? new List<Wait>();
            WaitType = WaitType.GroupWaitAll; // Default to MatchAll, can be changed by caller
        }

        internal GroupWait()
        {
        }

        internal Func<bool> GroupMatchFilter { get; set; }
        internal Delegate GroupMatchFilterOriginal { get; set; }
        internal string? HandlerKey { get; set; }


        /// <summary>
        /// Check if group is matched
        /// You should not update/mutate state in this method
        /// </summary>
        /// <param name="groupMatchFilter"></param>
        /// <param name="inCodeLine"></param>
        /// <param name="callerName"></param>
        /// <returns></returns>
        public Wait MatchIf(
            Func<bool> groupMatchFilter,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "",
            [CallerArgumentExpression(nameof(groupMatchFilter))] string? expression = default)
        {
            WaitType = WaitType.GroupWaitWithExpression;
            InCodeLine = inCodeLine;
            CallerName = callerName;
            GroupMatchFilter = groupMatchFilter;
            GroupMatchFilterOriginal = groupMatchFilter;
            HandlerKey = Helpers.WorkflowHashCalculator.CalculateHash(expression, callerName, "GroupMatch_" + WaitName);
            return this;
        }

        public Wait MatchIf<TState>(
            Func<TState, bool> groupMatchFilter,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "",
            [CallerArgumentExpression(nameof(groupMatchFilter))] string? expression = default)
        {
            WaitType = WaitType.GroupWaitWithExpression;
            InCodeLine = inCodeLine;
            CallerName = callerName;
            GroupMatchFilter = new StatefulGroupMatchInvoker<TState>(this, groupMatchFilter).Invoke;
            GroupMatchFilterOriginal = groupMatchFilter;
            HandlerKey = Helpers.WorkflowHashCalculator.CalculateHash(expression, callerName, "GroupMatch_" + WaitName);
            return this;
        }

        public Wait MatchAll()
        {
            WaitType = WaitType.GroupWaitAll;
            return this;
        }

        public Wait MatchFirst() => MatchAny();
        public Wait MatchAny()
        {
            WaitType = WaitType.GroupWaitFirst;
            return this;
        }

        public GroupWait WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens.Add(token);
            return this;
        }

        private sealed class StatefulGroupMatchInvoker<TState>
        {
            private readonly GroupWait _wait;
            private readonly Func<TState, bool> _predicate;

            public StatefulGroupMatchInvoker(GroupWait wait, Func<TState, bool> predicate)
            {
                _wait = wait;
                _predicate = predicate;
            }

            public bool Invoke()
            {
                return _predicate((TState)_wait.ExplicitState);
            }
        }
    }
}
