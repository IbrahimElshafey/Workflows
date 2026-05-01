
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
        internal DTOs.WaitsGroupDto WaitsGroupEntity { get; }
        internal IReadOnlyList<Wait> ChildWaitsRuntime { get; }

        internal GroupWait(DTOs.WaitsGroupDto wait, IReadOnlyList<Wait> childWaits = null) : base(wait)
        {
            WaitsGroupEntity = wait;
            ChildWaitsRuntime = childWaits;
        }

        public int CompletedCount => WaitsGroupEntity.ChildWaits?.Count(x => x.Status == Enums.WaitStatus.Completed) ?? 0;

        /// <summary>
        /// Check if group is matched
        /// You should not update/mutate state in this method
        /// </summary>
        /// <param name="groupMatchFilter"></param>
        /// <param name="inCodeLine"></param>
        /// <param name="callerName"></param>
        /// <returns></returns>
        public Wait MatchIf(
        Func<Definition.GroupWait, bool> groupMatchFilter,
        [CallerLineNumber] int inCodeLine = 0,
        [CallerMemberName] string callerName = "")
        {
            WaitsGroupEntity.WaitType = Enums.WaitType.GroupWaitWithExpression;
            WaitsGroupEntity.InCodeLine = inCodeLine;
            WaitsGroupEntity.CallerName = callerName;
            WaitsGroupEntity.MatchFuncName = ValidateCallback(groupMatchFilter, nameof(WaitsGroupEntity.MatchFuncName));
            return this;
        }

        public Wait MatchAll()
        {
            WaitsGroupEntity.WaitType = Enums.WaitType.GroupWaitAll;
            return this;
        }

        public Wait MatchFirst() => MatchAny();
        public Wait MatchAny()
        {
            WaitsGroupEntity.WaitType = Enums.WaitType.GroupWaitFirst;
            return this;
        }
        public HashSet<string> CancelTokens { get; set; }

        public Definition.GroupWait WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens ??= new HashSet<string>();
            CancelTokens.Add(token);
            return this;
        }

        IPassiveWait IPassiveWait.WithCancelToken(string token) => WithCancelToken(token);
    }
}