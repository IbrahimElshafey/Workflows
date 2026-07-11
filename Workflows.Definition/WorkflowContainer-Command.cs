using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Workflows.Abstraction.Runner;
using Workflows.Primitives;

namespace Workflows.Definition
{
    public abstract partial class WorkflowContainer
    {
        protected ImmediateCommandBuilder<TCommand, TResult> ExecuteImmediate<TCommand, TResult>(
            string commandName,
            TCommand data,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
            where TCommand : IImmediateCommand<TCommand, TResult>
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                throw new ArgumentException("Command name must not be null or empty", nameof(commandName));
            }

            var commandWait = new ImmediateCommandWait<TCommand, TResult>(
                commandName,
                data,
                inCodeLine,
                callerName,
                callerFilePath)
            {
                WorkflowContainer = this,
                WaitType = WaitType.Command
            };

            return new ImmediateCommandBuilder<TCommand, TResult>(commandWait);
        }

        protected DeferredCommandBuilder<TCommand, TResult> ExecuteDeferred<TCommand, TResult>(
            string commandName,
            TCommand data,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
            where TCommand : IDeferredCommand<TCommand, TResult>
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                throw new ArgumentException("Command name must not be null or empty", nameof(commandName));
            }

            var commandWait = new DeferredCommandWait<TCommand, TResult>(
                commandName,
                data,
                inCodeLine,
                callerName,
                callerFilePath)
            {
                WorkflowContainer = this,
                WaitType = WaitType.Command
            };

            // Auto-detect matching function from the interface
            var deferred = (IDeferredCommand<TCommand, TResult>)(object)data!;
            commandWait.MatchExpression = deferred.MatchingFunction;
            commandWait.MatchExpressionAsText = deferred.MatchingFunction?.ToString();
            commandWait.MatchTemplateHashKey = Helpers.WorkflowHashCalculator.CalculateHash(
                commandWait.MatchExpressionAsText, callerName, "Match_" + commandName);

            return new DeferredCommandBuilder<TCommand, TResult>(commandWait);
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
