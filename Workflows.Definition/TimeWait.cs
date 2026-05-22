using System;
using System.Threading.Tasks;
using Workflows.Primitives;

namespace Workflows.Definition
{
    public class TimeWait : Wait
    {
        internal TimeWait(string waitName, TimeSpan timeToWait, string uniqueMatchId, int inCodeLine, string callerName, string callerFilePath)
            : base(WaitType.SignalWait, waitName, inCodeLine, callerName, callerFilePath)
        {
            TimeToWait = timeToWait;
            UniqueMatchId = uniqueMatchId;
        }

        internal Delegate AfterMatchAction { get; set; }
        internal TimeSpan TimeToWait { get; set; }
        internal string UniqueMatchId { get; set; }

        public TimeWait WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens.Add(token);
            return this;
        }

        public TimeWait AfterMatch(Action action)
        {
            AfterMatchAction = action;
            return this;
        }

        public TimeWait AfterMatch(Func<ValueTask> action)
        {
            AfterMatchAction = action;
            return this;
        }
    }
}
