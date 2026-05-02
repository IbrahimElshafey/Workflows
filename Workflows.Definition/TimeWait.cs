using System;
using System.Collections.Generic;

namespace Workflows.Definition
{
    public class TimeWait : Wait, IPassiveWait
    {
        internal TimeWait(string waitName, TimeSpan timeToWait, string uniqueMatchId, int inCodeLine, string callerName)
            : base(WaitType.SignalWait, waitName, inCodeLine, callerName)
        {
            TimeToWait = timeToWait;
            UniqueMatchId = uniqueMatchId;
        }

        internal TimeSpan TimeToWait { get; set; }
        internal string UniqueMatchId { get; set; }
        internal string CancelActionSerialized { get; set; }

        public HashSet<string> CancelTokens { get; set; }

        public TimeWait WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens ??= new HashSet<string>();
            CancelTokens.Add(token);
            return this;
        }

        IPassiveWait IPassiveWait.WithCancelToken(string token) => WithCancelToken(token);
    }
}
