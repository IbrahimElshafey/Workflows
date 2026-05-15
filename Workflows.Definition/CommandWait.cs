using System;
using System.Threading.Tasks;
using Workflows.Primitives;

namespace Workflows.Definition
{
    public readonly struct CommandBuilder<TCommand, TResult>
    {
        private readonly CommandWait<TCommand, TResult> _wait;

        internal CommandBuilder(CommandWait<TCommand, TResult> wait) => _wait = wait;

        public CommandBuilder<TCommand, TResult> WithRetries(int maxAttempts, TimeSpan? backoff = null)
        {
            _wait.WithRetries(maxAttempts, backoff);
            return this;
        }

        public CommandBuilder<TCommand, TResult> OnResult(Action<TResult> onSuccess)
        {
            _wait.OnResult(onSuccess);
            return this;
        }

        public CommandBuilder<TCommand, TResult> OnFailure(Func<Exception, ValueTask> failureAction)
        {
            _wait.OnFailure(failureAction);
            return this;
        }

        public CommandBuilder<TCommand, TResult> RegisterCompensation(Func<TResult, ValueTask> compensationAction)
        {
            _wait.RegisterCompensation(compensationAction);
            return this;
        }

        public CommandBuilder<TCommand, TResult> WithToken(params string[] tokens)
        {
            _wait.WithToken(tokens);
            return this;
        }

        public CommandBuilder<TCommand, TResult> WithHandlerKey(string key, CommandExecutionMode mode = CommandExecutionMode.Direct)
        {
            _wait.WithHandlerKey(key, mode);
            return this;
        }

        public CommandBuilder<TCommand, TResult> WithExecutionMode(CommandExecutionMode mode)
        {
            _wait.WithExecutionMode(mode);
            return this;
        }

        public StatefulCommandBuilder<TCommand, TResult, TState> WithState<TState>(TState state)
        {
            _wait.ExplicitState = state;
            return new StatefulCommandBuilder<TCommand, TResult, TState>(_wait);
        }

        public CommandWait<TCommand, TResult> Build() => _wait;

        public static implicit operator CommandWait<TCommand, TResult>(CommandBuilder<TCommand, TResult> builder) => builder._wait;
        public static implicit operator Wait(CommandBuilder<TCommand, TResult> builder) => builder._wait;
    }

    public readonly struct StatefulCommandBuilder<TCommand, TResult, TState>
    {
        private readonly CommandWait<TCommand, TResult> _wait;

        internal StatefulCommandBuilder(CommandWait<TCommand, TResult> wait) => _wait = wait;

        public StatefulCommandBuilder<TCommand, TResult, TState> WithRetries(int maxAttempts, TimeSpan? backoff = null)
        {
            _wait.WithRetries(maxAttempts, backoff);
            return this;
        }

        public StatefulCommandBuilder<TCommand, TResult, TState> OnResult(Action<TResult, TState> onSuccess)
        {
            _wait.OnResult(onSuccess);
            return this;
        }

        public StatefulCommandBuilder<TCommand, TResult, TState> OnResult(Action<TResult> onSuccess)
        {
            _wait.OnResult(onSuccess);
            return this;
        }

        public StatefulCommandBuilder<TCommand, TResult, TState> OnFailure(Func<Exception, TState, ValueTask> failureAction)
        {
            _wait.OnFailure(failureAction);
            return this;
        }

        public StatefulCommandBuilder<TCommand, TResult, TState> OnFailure(Func<Exception, ValueTask> failureAction)
        {
            _wait.OnFailure(failureAction);
            return this;
        }

        public StatefulCommandBuilder<TCommand, TResult, TState> RegisterCompensation(Func<TResult, TState, ValueTask> compensationAction)
        {
            _wait.RegisterCompensation(compensationAction);
            return this;
        }

        public StatefulCommandBuilder<TCommand, TResult, TState> RegisterCompensation(Func<TResult, ValueTask> compensationAction)
        {
            _wait.RegisterCompensation(compensationAction);
            return this;
        }

        public StatefulCommandBuilder<TCommand, TResult, TState> WithToken(params string[] tokens)
        {
            _wait.WithToken(tokens);
            return this;
        }

        public StatefulCommandBuilder<TCommand, TResult, TState> WithHandlerKey(string key, CommandExecutionMode mode = CommandExecutionMode.Direct)
        {
            _wait.WithHandlerKey(key, mode);
            return this;
        }

        public StatefulCommandBuilder<TCommand, TResult, TState> WithExecutionMode(CommandExecutionMode mode)
        {
            _wait.WithExecutionMode(mode);
            return this;
        }

        public CommandWait<TCommand, TResult> Build() => _wait;

        public static implicit operator CommandWait<TCommand, TResult>(StatefulCommandBuilder<TCommand, TResult, TState> builder) => builder._wait;
        public static implicit operator Wait(StatefulCommandBuilder<TCommand, TResult, TState> builder) => builder._wait;
    }

    public class CommandWait<TCommand, TResult> : Wait, ICommandWait
    {
        internal bool IsCompensated { get; set; }
        internal TCommand CommandData { get; set; }
        internal Func<Exception, ValueTask> OnFailureAction { get; set; }
        internal Action<TResult> OnResultAction { get; set; }
        internal Func<TResult, ValueTask> CompensationAction { get; set; }
        internal string[] CompensationTokens { get; set; }
        internal int MaxRetryAttempts { get; set; } = 1;
        internal TimeSpan? RetryBackoff { get; set; }
        internal string HandlerKey { get; set; }
        internal CommandExecutionMode ExecutionMode { get; set; } = CommandExecutionMode.Direct;

        internal CommandWait(string commandName, TCommand data, int inCodeLine, string caller, string callerFilePath)
            : base(WaitType.Command, commandName, inCodeLine, caller, callerFilePath)
        {
            CommandData = data;
        }

        public CommandWait<TCommand, TResult> WithState<TState>(TState state)
        {
            ExplicitState = state;
            return this;
        }

        public CommandWait<TCommand, TResult> OnResult<TState>(Action<TResult, TState> onSuccess)
        {
            OnResultAction = new StatefulOnResultInvoker<TState>(this, onSuccess).Invoke;
            return this;
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
        public CommandWait<TCommand, TResult> OnFailure(Func<Exception, ValueTask> failureAction)
        {
            OnFailureAction = failureAction;
            return this;
        }

        public CommandWait<TCommand, TResult> OnFailure<TState>(Func<Exception, TState, ValueTask> failureAction)
        {
            OnFailureAction = new StatefulOnFailureInvoker<TState>(this, failureAction).Invoke;
            return this;
        }

        public CommandWait<TCommand, TResult> WithToken(params string[] tokens)
        {
            CompensationTokens = tokens;
            return this;
        }
        public CommandWait<TCommand, TResult> RegisterCompensation(Func<TResult,ValueTask> compensationAction)
        {
            CompensationAction = compensationAction;
            return this;
        }

        public CommandWait<TCommand, TResult> RegisterCompensation<TState>(Func<TResult, TState, ValueTask> compensationAction)
        {
            CompensationAction = new StatefulCompensationInvoker<TState>(this, compensationAction).Invoke;
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

        private sealed class StatefulOnResultInvoker<TState>
        {
            private readonly CommandWait<TCommand, TResult> _wait;
            private readonly Action<TResult, TState> _action;

            public StatefulOnResultInvoker(CommandWait<TCommand, TResult> wait, Action<TResult, TState> action)
            {
                _wait = wait;
                _action = action;
            }

            public void Invoke(TResult result)
            {
                _action(result, (TState)_wait.ExplicitState);
            }
        }

        private sealed class StatefulOnFailureInvoker<TState>
        {
            private readonly CommandWait<TCommand, TResult> _wait;
            private readonly Func<Exception, TState, ValueTask> _action;

            public StatefulOnFailureInvoker(CommandWait<TCommand, TResult> wait, Func<Exception, TState, ValueTask> action)
            {
                _wait = wait;
                _action = action;
            }

            public ValueTask Invoke(Exception exception)
            {
                return _action(exception, (TState)_wait.ExplicitState);
            }
        }

        private sealed class StatefulCompensationInvoker<TState>
        {
            private readonly CommandWait<TCommand, TResult> _wait;
            private readonly Func<TResult, TState, ValueTask> _action;

            public StatefulCompensationInvoker(CommandWait<TCommand, TResult> wait, Func<TResult, TState, ValueTask> action)
            {
                _wait = wait;
                _action = action;
            }

            public ValueTask Invoke(TResult result)
            {
                return _action(result, (TState)_wait.ExplicitState);
            }
        }
    }
}

