using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using System;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Enums;
namespace Workflows.Handler.BaseUse
{
    /// <summary>
    /// Represents a composite group of passive waits that can be combined
    /// using MatchAll(), MatchAny(), or custom MatchIf() logic.
    /// </summary>
    public class GroupWait : Wait, IPassiveWait
    {
        internal WaitsGroupDto WaitsGroupEntity { get; }
        internal IReadOnlyList<Wait> ChildWaitsRuntime { get; }

        internal GroupWait(WaitsGroupDto wait, IReadOnlyList<Wait> childWaits = null) : base(wait)
        {
            WaitsGroupEntity = wait;
            ChildWaitsRuntime = childWaits;
        }

        public int CompletedCount => WaitsGroupEntity.ChildWaits?.Count(x => x.Status == WaitStatus.Completed) ?? 0;

        /// <summary>
        /// Check if group is matched
        /// You should not update/mutate state in this method
        /// </summary>
        /// <param name="groupMatchFilter"></param>
        /// <param name="inCodeLine"></param>
        /// <param name="callerName"></param>
        /// <returns></returns>
        public Wait MatchIf(
        Func<GroupWait, bool> groupMatchFilter,
        [CallerLineNumber] int inCodeLine = 0,
        [CallerMemberName] string callerName = "")
        {
            WaitsGroupEntity.WaitType = WaitType.GroupWaitWithExpression;
            WaitsGroupEntity.InCodeLine = inCodeLine;
            WaitsGroupEntity.CallerName = callerName;
            WaitsGroupEntity.MatchFuncName = ValidateCallback(groupMatchFilter, nameof(WaitsGroupEntity.MatchFuncName));
            return this;
        }

        public Wait MatchAll()
        {
            WaitsGroupEntity.WaitType = WaitType.GroupWaitAll;
            return this;
        }

        public Wait MatchFirst() => MatchAny();
        public Wait MatchAny()
        {
            WaitsGroupEntity.WaitType = WaitType.GroupWaitFirst;
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