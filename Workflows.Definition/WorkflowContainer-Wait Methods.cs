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

        /// <summary>
        /// Creates a group of passive waits that can use any matching strategy (MatchAll, MatchAny, MatchIf).
        /// Only passive waits (signals, timers, sub-workflows) are allowed to prevent race conditions.
        /// </summary>
        protected GroupWait WaitGroup(
            IPassiveWait[] passiveWaits,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (passiveWaits.Any(x => x == null))
            {
                throw new ArgumentNullException($"The group wait named [{name}] contains wait that is null.");
            }

            // Convert IPassiveWait markers to Wait instances for internal use
            var waits = passiveWaits.Cast<Wait>().ToArray();
            
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

        /// <summary>
        /// Creates a parallel group of active commands that execute with MatchAll semantics.
        /// Commands cannot use MatchAny() to prevent multiple commands from executing in a race condition.
        /// This method enforces MatchAll() immediately to prevent the workflow author from changing it.
        /// </summary>
        protected GroupWait ExecuteParallel(
            IActiveWait[] commands,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (commands.Any(x => x == null))
            {
                throw new ArgumentNullException($"The parallel command group named [{name}] contains a command that is null.");
            }

            // Convert IActiveWait markers to Wait instances for internal use
            var waits = commands.Cast<Wait>().ToArray();

            var group = new GroupWait(
                new WaitsGroupDto
                {
                    WaitName = name ?? $"#Parallel Commands `{inCodeLine}` by `{callerName}`",
                    ChildWaits = waits.Select(x => x.WaitData).ToList(),
                    WaitType = WaitType.GroupWaitAll,  // Always MatchAll for safety
                    InCodeLine = inCodeLine,
                    CallerName = callerName,
                    Created = DateTime.UtcNow
                },
                waits)
            ;
            group.CurrentWorkflow = this;
            group.WaitData.ChildWaits.ForEach(wait => wait.ParentWaitId = group.WaitData.Id);

            // Instantly call MatchAll() and return - prevents workflow author from changing matching strategy
            return group.MatchAll() as GroupWait;
        }

        /// <summary>
        /// Creates a single command wait that can be configured with retry, compensation, and result handlers.
        /// </summary>
        protected CommandWait<TCommand, TResult> ExecuteCommand<TCommand, TResult>(
            string commandName,
            TCommand data,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                throw new ArgumentException("Command name must not be null or empty", nameof(commandName));
            }

            var commandWait = new CommandWait<TCommand, TResult>(commandName, data)
            {
                CurrentWorkflow = this
            };
            commandWait.Data.InCodeLine = inCodeLine;
            commandWait.Data.CallerName = callerName;

            return commandWait;
        }
    }
}