using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Workflows.Primitives;

namespace Workflows.Definition
{
    public abstract partial class WorkflowContainer
    {
        protected SignalWait<SignalData> WaitSignal<SignalData>(
            string signalIdentifier,
            string name = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            var newSignalWait = new SignalWait<SignalData>(
                signalIdentifier,
                name,
                inCodeLine,
                callerName,
                callerFilePath)
            {
                WorkflowContainer = this
            };
            return newSignalWait;
        }

        protected GroupWait WaitGroup(
            IPassiveWait[] passiveWaits,
            string name = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (passiveWaits.Any(x => x == null))
            {
                throw new ArgumentNullException($"The group wait named [{name}] contains wait that is null.");
            }

            var waits = passiveWaits.Cast<Wait>().ToArray();

            var group = new GroupWait(
                name ?? $"#Wait Group `{inCodeLine}` by `{callerName}`",
                waits,
                inCodeLine,
                callerName,
                callerFilePath)
            {
                WorkflowContainer = this,
                WaitType = WaitType.GroupWaitAll
            };
            return group;
        }
    }
}
