using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FastExpressionCompiler;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Persistence;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.CompletionChecker
{
    /// <summary>
    /// Evaluates deferred command results on integration callback return.
    /// Works purely with DTOs - no Wait object conversion needed.
    /// </summary>
    internal class CommandCompletionChecker : WaitCompletionChecker
    {
        private readonly WorkflowExecutionContext _context;
        private readonly ICallbackRegistry _callbackRegistry;
        private readonly ITemplateRepository? _templateRepository;
        private static readonly ConcurrentDictionary<string, Action<object, object, object>> _compiledActions = new();

        public CommandCompletionChecker(
            WorkflowExecutionContext context, 
            ICallbackRegistry callbackRegistry,
            ITemplateRepository? templateRepository = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _callbackRegistry = callbackRegistry ?? throw new ArgumentNullException(nameof(callbackRegistry));
            _templateRepository = templateRepository;
        }

        public override async Task<bool> IsCompleted(WaitInfrastructureDto waitDto)
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
                    if (_context.WorkflowState?.StateObject?.Locals != null)
                    {
                        if (!_context.WorkflowState.StateObject.Locals.TryGetValue(commandWaitDto.StateKey.ToString(), out explicitState))
                        {
                            _context.WorkflowState.StateObject.Locals.TryGetValue(commandWaitDto.Id.ToString(), out explicitState);
                        }
                    }

                    ExecuteOnResultAction(commandWaitDto.ResultAction, result, explicitState);
                }
            }

            // Mark this wait as completed
            commandWaitDto.Status = WaitStatus.Completed;

            return true;
        }

        private string? GetDeferredCommandAction(string key)
        {
            if (_templateRepository != null)
            {
                var dbTemplate = _templateRepository.GetTemplate(key);
                return dbTemplate?.AfterMatchAction;
            }
            return null;
        }

        private void ExecuteOnResultAction(string resultActionKey, object result, object explicitState)
        {
            // Fast path: try the CallbackRegistry first (stable handlerKey:OnResult — no reflection).
            if (_callbackRegistry.TryGet(resultActionKey, out var registeredDelegate) && registeredDelegate != null)
            {
                var method = registeredDelegate.Method;
                var target = registeredDelegate.Target;

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var resultParam   = Expression.Parameter(typeof(object), "result");
                var stateParam    = Expression.Parameter(typeof(object), "state");

                Expression targetExpr = method.IsStatic
                    ? null!
                    : target != null && !(target is WorkflowContainer)
                        ? Expression.Constant(target)
                        : Expression.Convert(instanceParam, method.DeclaringType!);

                var parameters = method.GetParameters();
                Expression call;
                if (parameters.Length == 0)
                {
                    call = Expression.Call(targetExpr, method);
                }
                else if (parameters.Length == 1)
                {
                    call = Expression.Call(targetExpr, method,
                        Expression.Convert(resultParam, parameters[0].ParameterType));
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
                    throw new InvalidOperationException($"Unsupported OnResultAction signature: {method}");
                }

                var action = Expression.Lambda<Action<object, object, object>>(call, instanceParam, resultParam, stateParam)
                                     .CompileFast();
                action?.Invoke(_context.WorkflowInstance, result, explicitState);
                return;
            }

            // Fallback: look up in the SQLite template repository.
            var methodPath = GetDeferredCommandAction(resultActionKey);
            if (!string.IsNullOrWhiteSpace(methodPath))
            {
                var action = _compiledActions.GetOrAdd(methodPath, path =>
                {
                    var method = Helpers.MethodResolver.ResolveMethod(path);
                    if (method == null) return null;
                    return Helpers.MethodResolver.BuildMethodInvoker(method);
                });

                action?.Invoke(_context.WorkflowInstance, result, explicitState);
            }
        }
    }
}

