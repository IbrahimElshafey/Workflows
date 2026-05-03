using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Workflows.Definition
{
    /// <summary>
    /// Represents a composite group of passive waits that can be combined
    /// using MatchAll(), MatchAny(), or custom MatchIf() logic.
    /// </summary>
    public class GroupWait : Wait, IPassiveWait
    {
        internal IReadOnlyList<Wait> ChildWaitsRuntime { get; }

        internal GroupWait(string waitName, IReadOnlyList<Wait> childWaits, int inCodeLine, string callerName, string callerFilePath)
            : base(WaitType.GroupWaitAll, waitName, inCodeLine, callerName, callerFilePath)
        {
            ChildWaitsRuntime = childWaits;
            ChildWaits = childWaits?.ToList() ?? new List<Wait>();
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
        public HashSet<string> CancelTokens { get; set; }

        public GroupWait WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens ??= new HashSet<string>();
            CancelTokens.Add(token);
            return this;
        }

        IPassiveWait IPassiveWait.WithCancelToken(string token) => WithCancelToken(token);
    }
}
