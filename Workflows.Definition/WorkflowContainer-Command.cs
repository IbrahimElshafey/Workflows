using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Workflows.Primitives;

namespace Workflows.Definition
{
    public abstract partial class WorkflowContainer
    {
        protected CommandBuilder<TCommand, TResult> ExecuteCommand<TCommand, TResult>(
            string commandName,
            TCommand data,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if(string.IsNullOrWhiteSpace(commandName))
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
                WorkflowContainer = this,
                WaitType = WaitType.Command
            };

            return new CommandBuilder<TCommand, TResult>(commandWait);
        }

        protected CompensationWait Compensate(
            string compasenationToken,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (string.IsNullOrWhiteSpace(compasenationToken))
            {
                throw new InvalidOperationException("Wait name is mandatory.");
            }
            return new CompensationWait(
                compasenationToken,
                WaitType.Compensation,
                inCodeLine,
                callerName,
                callerFilePath)
            {
                WorkflowContainer = this,
                WaitType = WaitType.Compensation
            };
        }
    }
}
