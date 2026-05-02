using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Workflows.Definition
{
    public abstract partial class WorkflowContainer
    {
        protected Definition.SignalWait<SignalData> WaitSignal<SignalData>(
            string signalIdentifier,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = ""
            )
        {
            var newSignalWait = new Definition.SignalWait<SignalData>(
                signalIdentifier,
                name,
                inCodeLine,
                callerName)
            {
                CurrentWorkflow = this
            };
            return newSignalWait;
        }

        protected Definition.GroupWait WaitGroup(
            Definition.IPassiveWait[] passiveWaits,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (passiveWaits.Any(x => x == null))
            {
                throw new ArgumentNullException($"The group wait named [{name}] contains wait that is null.");
            }

            var waits = passiveWaits.Cast<Definition.Wait>().ToArray();

            var group = new Definition.GroupWait(
                name ?? $"#Wait Group `{inCodeLine}` by `{callerName}`",
                waits,
                inCodeLine,
                callerName)
            {
                CurrentWorkflow = this,
                WaitType = WaitType.GroupWaitAll
            };
            group.ChildWaits.ForEach(wait => wait.ParentWaitId = group.Id);
            return group;
        }

        protected Definition.GroupWait ExecuteParallel(
            Definition.ICommandWait[] commands,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (commands.Any(x => x == null))
            {
                throw new ArgumentNullException($"The parallel command group named [{name}] contains a command that is null.");
            }

            var waits = commands.Cast<Definition.Wait>().ToArray();

            var group = new Definition.GroupWait(
                name ?? $"#Parallel Commands `{inCodeLine}` by `{callerName}`",
                waits,
                inCodeLine,
                callerName)
            {
                CurrentWorkflow = this,
                WaitType = WaitType.CommandsGroup
            };
            group.ChildWaits.ForEach(wait => wait.ParentWaitId = group.Id);

            return group.MatchAll() as Definition.GroupWait;
        }

        protected Definition.CommandWait<TCommand, TResult> ExecuteCommand<TCommand, TResult>(
            string commandName,
            TCommand data,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                throw new ArgumentException("Command name must not be null or empty", nameof(commandName));
            }

            var commandWait = new Definition.CommandWait<TCommand, TResult>(commandName, data)
            {
                CurrentWorkflow = this,
                InCodeLine = inCodeLine,
                CallerName = callerName
            };

            return commandWait;
        }
    }
}
