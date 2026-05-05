using System;
using System.Threading.Tasks;
using Workflows.Shared.DataObject;

namespace Workflows.Definition
{
    public class CommandWait<TCommand, TResult> : Wait, ICommandWait
    {
        internal TCommand CommandData { get; set; }
        internal Action<TResult> OnResultAction { get; set; }
        internal Func<ValueTask> CompensationAction { get; set; }
        internal int MaxRetryAttempts { get; set; } = 1;
        internal TimeSpan? RetryBackoff { get; set; }
        internal string HandlerKey { get; set; }
        internal CommandExecutionMode ExecutionMode { get; set; } = CommandExecutionMode.Direct;

        internal CommandWait(string commandName, TCommand data, int inCodeLine, string caller, string callerFilePath)
            : base(WaitType.Command, commandName, inCodeLine, caller, callerFilePath)
        {
            CommandData = data;
        }

        public CommandWait<TCommand, TResult> OnResult(Action<TResult> onSuccess)
        {
            OnResultAction = onSuccess;
            return this;
        }


        public CommandWait<TCommand, TResult> WithRetries(int maxAttempts, TimeSpan? backoff = null)
        {
            if (maxAttempts < 1)
            {
                throw new ArgumentException("Max attempts must be at least 1", nameof(maxAttempts));
            }
            MaxRetryAttempts = maxAttempts;
            RetryBackoff = backoff;
            return this;
        }

        public CommandWait<TCommand, TResult> RegisterCompensation(Func<ValueTask> compensationAction)
        {
            CompensationAction = compensationAction;
            return this;
        }

        string ICommandWait.HandlerKey => HandlerKey;

        CommandExecutionMode ICommandWait.ExecutionMode => ExecutionMode;

        public CommandWait<TCommand, TResult> WithHandlerKey(string key, CommandExecutionMode mode = CommandExecutionMode.Direct)
        {
            HandlerKey = key;
            ExecutionMode = mode;
            return this;
        }

        public CommandWait<TCommand, TResult> WithExecutionMode(CommandExecutionMode mode)
        {
            ExecutionMode = mode;
            return this;
        }
    }
}

