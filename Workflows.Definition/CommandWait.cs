using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition.Helpers;
using Workflows.Primitives;

namespace Workflows.Definition
{
    // ==========================================
    // 1. TYPED IMMEDIATE BUILDERS
    // ==========================================
    public readonly struct ImmediateCommandBuilder<TCommand, TResult>
    {
        private readonly ImmediateCommandWait<TCommand, TResult> _wait;

        internal ImmediateCommandBuilder(ImmediateCommandWait<TCommand, TResult> wait) => _wait = wait;

        public ImmediateCommandBuilder<TCommand, TResult> WithRetries(int maxAttempts, TimeSpan? backoff = null)
        {
            _wait.WithRetries(maxAttempts, backoff);
            return this;
        }

        public ImmediateCommandBuilder<TCommand, TResult> OnResult(Action<TResult> onSuccess)
        {
            _wait.OnResult(onSuccess);
            return this;
        }

        public ImmediateCommandBuilder<TCommand, TResult> OnFailure(Func<Exception, ValueTask> failureAction)
        {
            _wait.OnFailure(failureAction);
            return this;
        }

        public ImmediateCommandBuilder<TCommand, TResult> RegisterCompensation(Func<TResult, ValueTask> compensationAction)
        {
            _wait.RegisterCompensation(compensationAction);
            return this;
        }

        public ImmediateCommandBuilder<TCommand, TResult> WithToken(params string[] tokens)
        {
            _wait.WithToken(tokens);
            return this;
        }

        public ImmediateCommandBuilder<TCommand, TResult> WithHandlerKey(string key)
        {
            _wait.WithHandlerKey(key);
            return this;
        }

        public StatefulImmediateCommandBuilder<TCommand, TResult, TState> WithState<TState>(TState state)
        {
            _wait.SetState(state);
            return new StatefulImmediateCommandBuilder<TCommand, TResult, TState>(_wait);
        }

        public ImmediateCommandWait<TCommand, TResult> Build() => _wait;

        public static implicit operator ImmediateCommandWait<TCommand, TResult>(ImmediateCommandBuilder<TCommand, TResult> builder) => builder._wait;
        public static implicit operator Wait(ImmediateCommandBuilder<TCommand, TResult> builder) => builder._wait;
        public static implicit operator WaitInfrastructureDto(ImmediateCommandBuilder<TCommand, TResult> builder) => WaitDtoConversion.Convert(builder._wait);
    }

    public readonly struct StatefulImmediateCommandBuilder<TCommand, TResult, TState>
    {
        private readonly ImmediateCommandWait<TCommand, TResult> _wait;

        internal StatefulImmediateCommandBuilder(ImmediateCommandWait<TCommand, TResult> wait) => _wait = wait;

        public StatefulImmediateCommandBuilder<TCommand, TResult, TState> WithRetries(int maxAttempts, TimeSpan? backoff = null)
        {
            _wait.WithRetries(maxAttempts, backoff);
            return this;
        }

        public StatefulImmediateCommandBuilder<TCommand, TResult, TState> OnResult(Action<TResult, TState> onSuccess)
        {
            _wait.OnResult(onSuccess);
            return this;
        }

        public StatefulImmediateCommandBuilder<TCommand, TResult, TState> OnResult(Action<TResult> onSuccess)
        {
            _wait.OnResult(onSuccess);
            return this;
        }

        public StatefulImmediateCommandBuilder<TCommand, TResult, TState> OnFailure(Func<Exception, TState, ValueTask> failureAction)
        {
            _wait.OnFailure(failureAction);
            return this;
        }

        public StatefulImmediateCommandBuilder<TCommand, TResult, TState> OnFailure(Func<Exception, ValueTask> failureAction)
        {
            _wait.OnFailure(failureAction);
            return this;
        }

        public StatefulImmediateCommandBuilder<TCommand, TResult, TState> RegisterCompensation(Func<TResult, TState, ValueTask> compensationAction)
        {
            _wait.RegisterCompensation(compensationAction);
            return this;
        }

        public StatefulImmediateCommandBuilder<TCommand, TResult, TState> RegisterCompensation(Func<TResult, ValueTask> compensationAction)
        {
            _wait.RegisterCompensation(compensationAction);
            return this;
        }

        public StatefulImmediateCommandBuilder<TCommand, TResult, TState> WithToken(params string[] tokens)
        {
            _wait.WithToken(tokens);
            return this;
        }

        public StatefulImmediateCommandBuilder<TCommand, TResult, TState> WithHandlerKey(string key)
        {
            _wait.WithHandlerKey(key);
            return this;
        }

        public ImmediateCommandWait<TCommand, TResult> Build() => _wait;

        public static implicit operator ImmediateCommandWait<TCommand, TResult>(StatefulImmediateCommandBuilder<TCommand, TResult, TState> builder) => builder._wait;
        public static implicit operator Wait(StatefulImmediateCommandBuilder<TCommand, TResult, TState> builder) => builder._wait;
        public static implicit operator WaitInfrastructureDto(StatefulImmediateCommandBuilder<TCommand, TResult, TState> builder) => WaitDtoConversion.Convert(builder._wait);
    }

    // ==========================================
    // 2. TYPED DEFERRED BUILDERS
    // ==========================================
    public readonly struct DeferredCommandBuilder<TCommand, TResult>
    {
        private readonly DeferredCommandWait<TCommand, TResult> _wait;

        internal DeferredCommandBuilder(DeferredCommandWait<TCommand, TResult> wait) => _wait = wait;

        public DeferredCommandBuilder<TCommand, TResult> WithRetries(int maxAttempts, TimeSpan? backoff = null)
        {
            _wait.WithRetries(maxAttempts, backoff);
            return this;
        }

        public DeferredCommandBuilder<TCommand, TResult> OnResult(Action<TResult> onSuccess)
        {
            _wait.OnResult(onSuccess);
            return this;
        }

        public DeferredCommandBuilder<TCommand, TResult> OnFailure(Func<Exception, ValueTask> failureAction)
        {
            _wait.OnFailure(failureAction);
            return this;
        }

        public DeferredCommandBuilder<TCommand, TResult> RegisterCompensation(Func<TResult, ValueTask> compensationAction)
        {
            _wait.RegisterCompensation(compensationAction);
            return this;
        }

        public DeferredCommandBuilder<TCommand, TResult> WithToken(params string[] tokens)
        {
            _wait.WithToken(tokens);
            return this;
        }

        public DeferredCommandBuilder<TCommand, TResult> WithHandlerKey(string key)
        {
            _wait.WithHandlerKey(key);
            return this;
        }

        public StatefulDeferredCommandBuilder<TCommand, TResult, TState> WithState<TState>(TState state)
        {
            _wait.SetState(state);
            return new StatefulDeferredCommandBuilder<TCommand, TResult, TState>(_wait);
        }

        public DeferredCommandBuilder<TCommand, TResult> MatchIf(
            Expression<Func<TCommand, TResult, bool>> matchExpression,
            [CallerMemberName] string callerName = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            _wait.MatchIf(matchExpression, callerName, callerLineNumber, expression);
            return this;
        }

        public DeferredCommandBuilder<TCommand, TResult> MatchIf<TState>(
            Expression<Func<TCommand, TResult, TState, bool>> matchExpression,
            [CallerMemberName] string callerName = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            _wait.MatchIf(matchExpression, callerName, callerLineNumber, expression);
            return this;
        }

        public DeferredCommandWait<TCommand, TResult> Build() => _wait;

        public static implicit operator DeferredCommandWait<TCommand, TResult>(DeferredCommandBuilder<TCommand, TResult> builder) => builder._wait;
        public static implicit operator Wait(DeferredCommandBuilder<TCommand, TResult> builder) => builder._wait;
        public static implicit operator WaitInfrastructureDto(DeferredCommandBuilder<TCommand, TResult> builder) => WaitDtoConversion.Convert(builder._wait);
    }

    public readonly struct StatefulDeferredCommandBuilder<TCommand, TResult, TState>
    {
        private readonly DeferredCommandWait<TCommand, TResult> _wait;

        internal StatefulDeferredCommandBuilder(DeferredCommandWait<TCommand, TResult> wait) => _wait = wait;

        public StatefulDeferredCommandBuilder<TCommand, TResult, TState> WithRetries(int maxAttempts, TimeSpan? backoff = null)
        {
            _wait.WithRetries(maxAttempts, backoff);
            return this;
        }

        public StatefulDeferredCommandBuilder<TCommand, TResult, TState> OnResult(Action<TResult, TState> onSuccess)
        {
            _wait.OnResult(onSuccess);
            return this;
        }

        public StatefulDeferredCommandBuilder<TCommand, TResult, TState> OnResult(Action<TResult> onSuccess)
        {
            _wait.OnResult(onSuccess);
            return this;
        }

        public StatefulDeferredCommandBuilder<TCommand, TResult, TState> OnFailure(Func<Exception, TState, ValueTask> failureAction)
        {
            _wait.OnFailure(failureAction);
            return this;
        }

        public StatefulDeferredCommandBuilder<TCommand, TResult, TState> OnFailure(Func<Exception, ValueTask> failureAction)
        {
            _wait.OnFailure(failureAction);
            return this;
        }

        public StatefulDeferredCommandBuilder<TCommand, TResult, TState> RegisterCompensation(Func<TResult, TState, ValueTask> compensationAction)
        {
            _wait.RegisterCompensation(compensationAction);
            return this;
        }

        public StatefulDeferredCommandBuilder<TCommand, TResult, TState> RegisterCompensation(Func<TResult, ValueTask> compensationAction)
        {
            _wait.RegisterCompensation(compensationAction);
            return this;
        }

        public StatefulDeferredCommandBuilder<TCommand, TResult, TState> WithToken(params string[] tokens)
        {
            _wait.WithToken(tokens);
            return this;
        }

        public StatefulDeferredCommandBuilder<TCommand, TResult, TState> WithHandlerKey(string key)
        {
            _wait.WithHandlerKey(key);
            return this;
        }

        public StatefulDeferredCommandBuilder<TCommand, TResult, TState> MatchIf(
            Expression<Func<TCommand, TResult, bool>> matchExpression,
            [CallerMemberName] string callerName = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            _wait.MatchIf(matchExpression, callerName, callerLineNumber, expression);
            return this;
        }

        public StatefulDeferredCommandBuilder<TCommand, TResult, TState> MatchIf(
            Expression<Func<TCommand, TResult, TState, bool>> matchExpression,
            [CallerMemberName] string callerName = "",
            [CallerLineNumber] int callerLineNumber = 0,
            [CallerArgumentExpression(nameof(matchExpression))] string? expression = default)
        {
            _wait.MatchIf(matchExpression, callerName, callerLineNumber, expression);
            return this;
        }

        public DeferredCommandWait<TCommand, TResult> Build() => _wait;

        public static implicit operator DeferredCommandWait<TCommand, TResult>(StatefulDeferredCommandBuilder<TCommand, TResult, TState> builder) => builder._wait;
        public static implicit operator Wait(StatefulDeferredCommandBuilder<TCommand, TResult, TState> builder) => builder._wait;
        public static implicit operator WaitInfrastructureDto(StatefulDeferredCommandBuilder<TCommand, TResult, TState> builder) => WaitDtoConversion.Convert(builder._wait);
    }

    // ==========================================
    // 3. Wait classes
    // ==========================================
    public abstract class CommandWait<TCommand, TResult> : Wait
    {
        internal bool IsCompensated { get; set; }
        internal TCommand CommandData { get; set; }
        internal Delegate OnFailureAction { get; set; }
        internal Delegate OnResultAction { get; set; }
        internal Delegate CompensationAction { get; set; }
        internal string[] CompensationTokens { get; set; }
        internal int MaxRetryAttempts { get; set; } = 1;
        internal TimeSpan? RetryBackoff { get; set; }
        internal string HandlerKey { get; set; }
        public abstract CommandExecutionMode ExecutionMode { get; }
        internal virtual LambdaExpression? MatchExpression { get; set; }
        internal virtual string? MatchExpressionAsText { get; set; }
        internal virtual string? MatchTemplateHashKey { get; set; }

        protected CommandWait(string commandName, TCommand data, int inCodeLine, string caller, string callerFilePath)
            : base(WaitType.Command, commandName, inCodeLine, caller, callerFilePath)
        {
            CommandData = data;
            HandlerKey = commandName;
        }

        internal CommandWait<TCommand, TResult> WithState<TState>(TState state)
        {
            SetState(state);
            return this;
        }

        internal CommandWait<TCommand, TResult> OnResult<TState>(Action<TResult, TState> onSuccess)
        {
            OnResultAction = onSuccess;
            return this;
        }

        internal CommandWait<TCommand, TResult> OnResult(Action<TResult> onSuccess)
        {
            OnResultAction = onSuccess;
            return this;
        }

        internal CommandWait<TCommand, TResult> WithRetries(int maxAttempts, TimeSpan? backoff = null)
        {
            if (maxAttempts < 1)
            {
                throw new ArgumentException("Max attempts must be at least 1", nameof(maxAttempts));
            }
            MaxRetryAttempts = maxAttempts;
            RetryBackoff = backoff;
            return this;
        }

        internal CommandWait<TCommand, TResult> OnFailure(Func<Exception, ValueTask> failureAction)
        {
            OnFailureAction = failureAction;
            return this;
        }

        internal CommandWait<TCommand, TResult> OnFailure<TState>(Func<Exception, TState, ValueTask> failureAction)
        {
            OnFailureAction = failureAction;
            return this;
        }

        internal CommandWait<TCommand, TResult> WithToken(params string[] tokens)
        {
            CompensationTokens = tokens;
            return this;
        }

        internal CommandWait<TCommand, TResult> RegisterCompensation(Func<TResult, ValueTask> compensationAction)
        {
            CompensationAction = compensationAction;
            return this;
        }

        internal CommandWait<TCommand, TResult> RegisterCompensation<TState>(Func<TResult, TState, ValueTask> compensationAction)
        {
            CompensationAction = compensationAction;
            return this;
        }

        internal CommandWait<TCommand, TResult> WithHandlerKey(string key)
        {
            HandlerKey = key;
            return this;
        }
    }

    public class ImmediateCommandWait<TCommand, TResult> : CommandWait<TCommand, TResult>
    {
        public override CommandExecutionMode ExecutionMode => CommandExecutionMode.Immediate;

        internal ImmediateCommandWait(string commandName, TCommand data, int inCodeLine, string caller, string callerFilePath)
            : base(commandName, data, inCodeLine, caller, callerFilePath)
        {
        }
    }

    public class DeferredCommandWait<TCommand, TResult> : CommandWait<TCommand, TResult>
    {
        public override CommandExecutionMode ExecutionMode => CommandExecutionMode.Deferred;
        internal override LambdaExpression? MatchExpression { get; set; }
        internal override string? MatchExpressionAsText { get; set; }
        internal override string? MatchTemplateHashKey { get; set; }

        internal DeferredCommandWait(string commandName, TCommand data, int inCodeLine, string caller, string callerFilePath)
            : base(commandName, data, inCodeLine, caller, callerFilePath)
        {
        }

        internal DeferredCommandWait<TCommand, TResult> MatchIf(
            Expression<Func<TCommand, TResult, bool>> matchExpression,
            string callerName = "",
            int callerLineNumber = 0,
            string? expression = default)
        {
            MatchExpression = matchExpression;
            InCodeLine = callerLineNumber;
            MatchExpressionAsText = expression;
            MatchTemplateHashKey = WorkflowHashCalculator.CalculateHash(expression, callerName, "Match_" + HandlerKey);
            return this;
        }

        internal DeferredCommandWait<TCommand, TResult> MatchIf<TState>(
            Expression<Func<TCommand, TResult, TState, bool>> matchExpression,
            string callerName = "",
            int callerLineNumber = 0,
            string? expression = default)
        {
            MatchExpression = matchExpression;
            InCodeLine = callerLineNumber;
            MatchExpressionAsText = expression;
            MatchTemplateHashKey = WorkflowHashCalculator.CalculateHash(expression, callerName, "Match_" + HandlerKey);
            return this;
        }
    }
}
