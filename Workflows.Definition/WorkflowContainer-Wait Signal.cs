using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Workflows.Primitives;

namespace Workflows.Definition
{
    public abstract partial class WorkflowContainer
    {
        protected SignalBuilder<SignalData> WaitSignal<SignalData>(
            string signalIdentifier,
            string name = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Wait name is mandatory.");
            }

            var newSignalWait = new SignalWait<SignalData>(
                signalIdentifier,
                name,
                inCodeLine,
                callerName,
                callerFilePath)
            {
                WorkflowContainer = this,
            };
            //newSignalWait.SetState(null);
            return new SignalBuilder<SignalData>(newSignalWait);
        }

        protected GroupWait WaitGroup(
            Wait[] passiveWaits,
            string name = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Wait name is mandatory.");
            }

            if (passiveWaits.Any(x => x == null))
            {
                throw new ArgumentNullException($"The group wait named [{name}] contains wait that is null.");
            }

            var waits = passiveWaits.Cast<Wait>().ToArray();

            var group = new GroupWait(
                name,
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
