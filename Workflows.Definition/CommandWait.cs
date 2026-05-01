using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Enums;

namespace Workflows.Handler.BaseUse
{
    /// <summary>
    /// Represents an active wait for an external command to complete.
    /// Commands are side-effecting operations that must execute,
    /// so they cannot be mixed with passive waits in MatchAny() scenarios.
    /// </summary>
    public class CommandWait<TCommand, TResult> : Wait, ICommandWait
    {
        internal Action<TResult> OnResultAction { get; set; }
        internal Action CompensationAction { get; set; }

        internal CommandWaitDto Data { get; }

        /// <summary>
        /// Internal constructor accepting command name and data.
        /// Initializes the CommandWaitDto with the serialized command payload.
        /// </summary>
        internal CommandWait(string commandName, TCommand data) : base(new CommandWaitDto
        {
            WaitName = commandName,
            WaitType = Workflows.Abstraction.Enums.WaitType.Command,
            Created = DateTime.UtcNow,
        })
        {
            Data = (CommandWaitDto)WaitData;
            // Serialize command data - caller will provide the serialization via a serializer instance
            // This is deferred to the workflow runner which has access to the IObjectSerializer
            Data.SerializedCommand = null; // Will be set by the workflow engine during execution
        }

        /// <summary>
        /// Registers a callback to execute when the command completes successfully.
        /// </summary>
        public CommandWait<TCommand, TResult> OnResult(Action<TResult> onSuccess)
        {
            OnResultAction = onSuccess;
            return this;
        }

        /// <summary>
        /// Registers a callback to execute when the command completes successfully.
        /// Supports async Task callbacks.
        /// </summary>
        public CommandWait<TCommand, TResult> OnResult(Func<TResult, Task> onSuccess)
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

        /// <summary>
        /// Updates the DTO retry settings.
        /// </summary>
        public CommandWait<TCommand, TResult> WithRetries(int maxAttempts, TimeSpan? backoff = null)
        {
            if (maxAttempts < 1)
            {
                throw new ArgumentException("Max attempts must be at least 1", nameof(maxAttempts));
            }
            Data.MaxRetryAttempts = maxAttempts;
            Data.RetryBackoff = backoff;
            return this;
        }

        /// <summary>
        /// Registers a compensation action to run if the workflow rolls back.
        /// </summary>
        public CommandWait<TCommand, TResult> RegisterCompensation(Action compensationAction)
        {
            CompensationAction = compensationAction;
            return this;
        }

        /// <summary>
        /// Registers a compensation action to run if the workflow rolls back.
        /// Supports async Task callbacks.
        /// </summary>
        public CommandWait<TCommand, TResult> RegisterCompensation(Func<Task> compensationAction)
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

        /// <inheritdoc/>
        string ICommandWait.HandlerKey => Data.HandlerKey;

        /// <inheritdoc/>
        CommandExecutionMode ICommandWait.ExecutionMode => Data.ExecutionMode;

        /// <summary>
        /// Sets the handler key used to resolve the command handler from ICommandHandlerFactory.
        /// </summary>
        public CommandWait<TCommand, TResult> WithHandlerKey(string key, CommandExecutionMode mode = CommandExecutionMode.Direct)
        {
            Data.HandlerKey = key;
            Data.ExecutionMode = mode;
            return this;
        }

        /// <summary>
        /// Sets the execution mode for this command (Fast or Slow).
        /// </summary>
        public CommandWait<TCommand, TResult> WithExecutionMode(CommandExecutionMode mode)
        {
            Data.ExecutionMode = mode;
            return this;
        }
    }
}
