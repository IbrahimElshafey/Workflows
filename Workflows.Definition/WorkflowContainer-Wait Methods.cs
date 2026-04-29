using Workflows.Handler.BaseUse;
using System.Runtime.CompilerServices;

using System;
using System.Linq;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.DTOs;
namespace Workflows.Handler
{
    public abstract partial class WorkflowContainer
    {

        protected SignalWait<SignalData> WaitSignal<SignalData>(
            string signalIdentifier,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = ""
            )
        {
            SignalWait<SignalData> newSignalWait = new SignalWait<SignalData>(new SignalWaitDto
            {
                SignalIdentifier = signalIdentifier,
                WaitName = name,
                WaitType = WaitType.SignalWait,
                InCodeLine = inCodeLine,
                CallerName = callerName,
                Created = DateTime.UtcNow,
            })
            {
                CurrentWorkflow = this
            };
            return newSignalWait;
        }

        protected GroupWait WaitGroup(
            Wait[] waits,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (waits.Any(x => x == null))
            {
                throw new ArgumentNullException($"The group wait named [{name}] contains wait that is null.");
            }
            var group = new GroupWait(
                new WaitsGroupDto
                {
                    WaitName = name ?? $"#Wait Group `{inCodeLine}` by `{callerName}`",
                    ChildWaits = waits.Select(x => x.WaitData).ToList(),
                    WaitType = WaitType.GroupWaitAll,
                    InCodeLine = inCodeLine,
                    CallerName = callerName,
                    Created = DateTime.UtcNow
                },
                waits)
            ;
            group.CurrentWorkflow = this;
            group.WaitData.ChildWaits.ForEach(wait => wait.ParentWaitId = group.WaitData.Id);
            return group;
        }
    }
}