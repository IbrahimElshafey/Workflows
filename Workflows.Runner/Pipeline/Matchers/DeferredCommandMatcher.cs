using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FastExpressionCompiler;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;

namespace Workflows.Runner.Pipeline.Matchers
{
    /// <summary>
    /// Evaluates deferred command results on integration callback return.
    /// Works purely with DTOs - no Wait object conversion needed.
    /// </summary>
    internal class DeferredCommandMatcher : WorkflowWaitMatcher
    {
        private readonly WorkflowExecutionContext _context;
        private readonly MatcherFactory _matcherFactory;
        private readonly IDelegateSerializer _delegateSerializer;
        private static readonly ConcurrentDictionary<string, Action<object, object, object>> _compiledActions = new();

        public DeferredCommandMatcher(
            WorkflowExecutionContext context, 
            MatcherFactory matcherFactory,
            IDelegateSerializer delegateSerializer)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _matcherFactory = matcherFactory ?? throw new ArgumentNullException(nameof(matcherFactory));
            _delegateSerializer = delegateSerializer ?? throw new ArgumentNullException(nameof(delegateSerializer));
        }

        public override async Task<bool> MatchAsync(WaitInfrastructureDto waitDto)
        {
            var commandWaitDto = waitDto as CommandWaitDto;
            if (commandWaitDto == null)
            {
                throw new InvalidOperationException("DeferredCommandMatcher requires a CommandWaitDto.");
            }

            var result = _context.CommandResult;

            // Handle failure scenarios
            if (result is Exception exception)
            {
                // Result is an exception - log or handle failure
                if (_context.WorkflowInstance != null)
                {
                    await _context.WorkflowInstance.OnError(
                        $"Deferred command failed: {exception.Message}", exception);
                }

                // TODO: Invoke OnFailureAction if we store it in DTO
            }
            else
            {
                // Success - invoke OnResultAction if we store it in DTO
                if (!string.IsNullOrWhiteSpace(commandWaitDto.ResultAction))
                {
                    // Retrieve explicitState
                    object explicitState = null;
                    if (_context.WorkflowState?.StateObject?.WaitStatesObjects != null)
                    {
                        if (!_context.WorkflowState.StateObject.WaitStatesObjects.TryGetValue(commandWaitDto.StateKey, out explicitState))
                        {
                            _context.WorkflowState.StateObject.WaitStatesObjects.TryGetValue(commandWaitDto.Id, out explicitState);
                        }
                    }

                    ExecuteOnResultAction(commandWaitDto.ResultAction, result, explicitState);
                }
            }

            // Mark this wait as completed
            commandWaitDto.Status = WaitStatus.Completed;

            // Propagate matching to parent wait if present
            if (commandWaitDto.ParentWaitId.HasValue)
            {
                return await MatchParentAsync(commandWaitDto.ParentWaitId.Value, _context, _matcherFactory);
            }

            return true;
        }

        private void ExecuteOnResultAction(string resultActionPath, object result, object explicitState)
        {
            var action = _compiledActions.GetOrAdd(resultActionPath, path =>
            {
                var method = _delegateSerializer.Deserialize(path);
                if (method == null) return null;

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var resultParam = Expression.Parameter(typeof(object), "result");
                var stateParam = Expression.Parameter(typeof(object), "state");

                var parameters = method.GetParameters();
                Expression call;

                var targetExpr = method.IsStatic ? null : Expression.Convert(instanceParam, method.DeclaringType);

                if (parameters.Length == 0)
                {
                    call = Expression.Call(targetExpr, method);
                }
                else if (parameters.Length == 1)
                {
                    call = Expression.Call(targetExpr, method, Expression.Convert(resultParam, parameters[0].ParameterType));
                }
                else if (parameters.Length == 2)
                {
                    var convertStateMethod = typeof(StateConverter).GetMethod(
                        nameof(StateConverter.ConvertState), 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    call = Expression.Call(targetExpr, method, 
                        Expression.Convert(resultParam, parameters[0].ParameterType),
                        Expression.Convert(
                            Expression.Call(convertStateMethod!, stateParam, Expression.Constant(parameters[1].ParameterType)),
                            parameters[1].ParameterType));
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported OnResultAction method signature: {method}");
                }

                var lambda = Expression.Lambda<Action<object, object, object>>(call, instanceParam, resultParam, stateParam);
                return lambda.CompileFast();
            });

            action?.Invoke(_context.WorkflowInstance, result, explicitState);
        }
    }
}

