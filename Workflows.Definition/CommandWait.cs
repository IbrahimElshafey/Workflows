using System;
using System.Threading.Tasks;

namespace Workflows.Definition
{
    public class CommandWait<TCommand, TResult> : Wait, ICommandWait
    {
        internal TResult CommandResult { get; set; }
        internal TCommand CommandData { get; set; }
        internal Action<TResult> OnResultAction { get; set; }
        internal Action CompensationAction { get; set; }

        internal int MaxRetryAttempts { get; set; } = 1;
        internal TimeSpan? RetryBackoff { get; set; }
        internal string CompensationMethodName { get; set; }
        internal string CancelActionSerialized { get; set; }
        internal string ResultActionSerialized { get; set; }
        internal string HandlerKey { get; set; }
        internal CommandExecutionMode ExecutionMode { get; set; } = CommandExecutionMode.Direct;

        internal CommandWait(string commandName, TCommand data) : base(WaitType.Command, commandName, 0, null)
        {
            CommandData = data;
        }

        public Definition.CommandWait<TCommand, TResult> OnResult(Action<TResult> onSuccess)
        {
            OnResultAction = onSuccess;
            return this;
        }

        public Definition.CommandWait<TCommand, TResult> OnResult(Func<TResult, Task> onSuccess)
        {
            if (onSuccess != null)
            {
                OnResultAction = result =>
                {
                    onSuccess(result).Wait();
                };
            }
            return this;
        }

        public Definition.CommandWait<TCommand, TResult> WithRetries(int maxAttempts, TimeSpan? backoff = null)
        {
            if (maxAttempts < 1)
            {
                throw new ArgumentException("Max attempts must be at least 1", nameof(maxAttempts));
            }
            MaxRetryAttempts = maxAttempts;
            RetryBackoff = backoff;
            return this;
        }

        public Definition.CommandWait<TCommand, TResult> RegisterCompensation(Action compensationAction)
        {
            CompensationAction = compensationAction;
            return this;
        }

        public Definition.CommandWait<TCommand, TResult> RegisterCompensation(Func<Task> compensationAction)
        {
            if (compensationAction != null)
            {
                CompensationAction = () =>
                {
                    compensationAction().Wait();
                };
            }
            return this;
        }

        string ICommandWait.HandlerKey => HandlerKey;

        CommandExecutionMode ICommandWait.ExecutionMode => ExecutionMode;

        public Definition.CommandWait<TCommand, TResult> WithHandlerKey(string key, CommandExecutionMode mode = CommandExecutionMode.Direct)
        {
            HandlerKey = key;
            ExecutionMode = mode;
            return this;
        }

        public Definition.CommandWait<TCommand, TResult> WithExecutionMode(CommandExecutionMode mode)
        {
            ExecutionMode = mode;
            return this;
        }
    }
}

