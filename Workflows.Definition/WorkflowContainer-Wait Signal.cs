using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Workflows.Definition
{
    public abstract partial class WorkflowContainer
    {
        protected CompensationWait Compensate(
            string compasenationToken,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            return new CompensationWait(
                compasenationToken,
                WaitType.Compensation,
                inCodeLine,
                callerName,
                callerFilePath)
            {
                WorkflowContainer = this
            };
        }
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

        protected Wait ExecuteParallel(
            ICommandWait[] commands,
            string name = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (commands.Any(x => x == null))
            {
                throw new ArgumentNullException(
                    $"The parallel command group named [{name}] contains a command that is null.");
            }

            var waits = commands.Cast<Wait>().ToArray();

            var group = new GroupWait(
                name ?? $"#Parallel Commands `{inCodeLine}` by `{callerName}`",
                waits,
                inCodeLine,
                callerName,
                callerFilePath)
            {
                WorkflowContainer = this,
                WaitType = WaitType.CommandsGroup
            };

            return group;
        }

        protected CommandWait<TCommand, TResult> ExecuteCommand<TCommand, TResult>(
            string commandName,
            TCommand data,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                throw new ArgumentException("Command name must not be null or empty", nameof(commandName));
            }

            var commandWait = new CommandWait<TCommand, TResult>(
                commandName,
                data,
                inCodeLine,
                callerName,
                callerFilePath)
            {
                WorkflowContainer = this
            };

            return commandWait;
        }
    }
}
