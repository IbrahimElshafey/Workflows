using System;
using System.Runtime.CompilerServices;

namespace Workflows.Definition
{
    public abstract partial class WorkflowContainer
    {
        protected Definition.TimeWait WaitUntil(
            DateTime untilTime,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (untilTime < DateTime.UtcNow)
            {
                throw new ArgumentException("Until date should be in the future", nameof(untilTime));
            }
            var timeToWait = untilTime - DateTime.UtcNow;
            Definition.TimeWait newTimeWait = new Definition.TimeWait(new DTOs.TimeWaitDto
            {
                WaitName = name ?? $"#Time Wait for `{timeToWait.TotalHours}` hours in `{callerName}`",
                TimeToWait = timeToWait,
                UniqueMatchId = Guid.NewGuid().ToString(),
                InCodeLine = inCodeLine,
                CallerName = callerName,
                Created = DateTime.UtcNow
            })
            {
                CurrentWorkflow = this
            };
            return newTimeWait;
        }

        protected Definition.TimeWait WaitDelay(
            TimeSpan timeToWait,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (timeToWait.TotalMilliseconds <= 0)
            {
                throw new ArgumentException("Time to wait should be greater than 0", nameof(timeToWait));
            }
            return new Definition.TimeWait(new DTOs.TimeWaitDto
            {
                WaitName = name ?? $"#Time Wait for `{timeToWait.TotalHours}` hours in `{callerName}`",
                TimeToWait = timeToWait,
                UniqueMatchId = Guid.NewGuid().ToString(),
                InCodeLine = inCodeLine,
                CallerName = callerName,
                Created = DateTime.UtcNow
            })
            {
                CurrentWorkflow = this
            };
        }

    }
}