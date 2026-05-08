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
    public class GroupWait : Wait, IPassiveWait
    {

        internal GroupWait(string waitName, IReadOnlyList<Wait> childWaits, int inCodeLine, string callerName, string callerFilePath)
            : base(WaitType.GroupWaitAll, waitName, inCodeLine, callerName, callerFilePath)
        {
            ChildWaits = childWaits?.ToList() ?? new List<Wait>();
            WaitType = WaitType.GroupWaitAll; // Default to MatchAll, can be changed by caller
            CancelTokens.Add($"GroupCancel_{Id}"); // Add waitName as a default cancel token for the group
        }

        internal Func<bool> GroupMatchFilter { get; set; }


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
        [CallerMemberName] string callerName = "")
        {
            WaitType = WaitType.GroupWaitWithExpression;
            InCodeLine = inCodeLine;
            CallerName = callerName;
            GroupMatchFilter = groupMatchFilter;
            return this;
        }

        public Wait MatchIf<TState>(
        Func<TState, bool> groupMatchFilter,
        [CallerLineNumber] int inCodeLine = 0,
        [CallerMemberName] string callerName = "")
        {
            WaitType = WaitType.GroupWaitWithExpression;
            InCodeLine = inCodeLine;
            CallerName = callerName;
            GroupMatchFilter = new StatefulGroupMatchInvoker<TState>(this, groupMatchFilter).Invoke;
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
        public HashSet<string> CancelTokens { get; set; } = new HashSet<string>();

        public GroupWait WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens ??= new HashSet<string>();
            CancelTokens.Add(token);
            return this;
        }

        IPassiveWait IPassiveWait.WithCancelToken(string token) => WithCancelToken(token);

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
