using System;
using System.Runtime.CompilerServices;
using Workflows.Abstraction.DTOs;
using Workflows.Handler.BaseUse;
namespace Workflows.Handler
{
    public abstract partial class WorkflowContainer
    {
        protected TimeWait WaitUntil(
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
            TimeWait newTimeWait = new TimeWait(new TimeWaitDto
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

        protected TimeWait WaitDelay(
            TimeSpan timeToWait,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (timeToWait.TotalMilliseconds <= 0)
            {
                throw new ArgumentException("Time to wait should be greater than 0", nameof(timeToWait));
            }
            return new TimeWait(new TimeWaitDto
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