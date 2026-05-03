using System;
using System.Runtime.CompilerServices;

namespace Workflows.Definition
{
    public abstract partial class WorkflowContainer
    {
        protected TimeWait WaitUntil(
            DateTime untilTime,
            string name = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (untilTime < DateTime.UtcNow)
            {
                throw new ArgumentException("Until date should be in the future", nameof(untilTime));
            }
            var timeToWait = untilTime - DateTime.UtcNow;
            TimeWait newTimeWait = new TimeWait(
                name ?? $"#Time Wait for `{timeToWait.TotalHours}` hours in `{callerName}`",
                timeToWait,
                Guid.NewGuid().ToString(), inCodeLine, callerName, callerFilePath)
            {
                WorkflowContainer = this
            };
            return newTimeWait;
        }

        protected TimeWait WaitDelay(
            TimeSpan timeToWait,
            string name = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (timeToWait.TotalMilliseconds <= 0)
            {
                throw new ArgumentException("Time to wait should be greater than 0", nameof(timeToWait));
            }
            return new TimeWait(
                name ?? $"#Time Wait for `{timeToWait.TotalHours}` hours in `{callerName}`",
                timeToWait,
                Guid.NewGuid().ToString(), inCodeLine, callerName, callerFilePath)
            {
                WorkflowContainer = this
            };
        }

    }
}
