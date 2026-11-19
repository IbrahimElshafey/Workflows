using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FastExpressionCompiler;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Runner.Cache;
using Workflows.Runner.ExpressionTransformers;
using IExpressionSerializer = Workflows.Abstraction.Helpers.IExpressionSerializer;
using ExpressionCompiler = Workflows.Runner.ExpressionTransformers.ExpressionCompiler;

namespace Workflows.Runner.Pipeline.CompletionChecker
{
    /// <summary>
    /// Matches incoming signal events against SignalWait constraints.
    /// Works purely with DTOs - no Wait object conversion needed.
    /// </summary>
    internal class SignalCompletionChecker : WaitCompletionChecker
    {
        internal static readonly ConcurrentDictionary<string, SignalTemplateCacheRecord> SignalCache = new();

        private readonly IWorkflowRegistry _workflowRegistry;
        private readonly WorkflowExecutionContext _context;
        private readonly IExpressionSerializer _expressionSerializer;
        private readonly MatchExpressionTransformer _matchExpressionTransformer;
        private readonly ICallbackRegistry _callbackRegistry;
        private readonly ITemplateRepository? _templateRepository;

        public SignalCompletionChecker(
            IWorkflowRegistry workflowRegistry, 
            WorkflowExecutionContext context,
            IExpressionSerializer expressionSerializer,
            MatchExpressionTransformer matchExpressionTransformer,
            ICallbackRegistry callbackRegistry,
            ITemplateRepository? templateRepository = null)
        {
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _expressionSerializer = expressionSerializer ?? throw new ArgumentNullException(nameof(expressionSerializer));
            _matchExpressionTransformer = matchExpressionTransformer ?? throw new ArgumentNullException(nameof(matchExpressionTransformer));
            _callbackRegistry = callbackRegistry ?? throw new ArgumentNullException(nameof(callbackRegistry));
            _templateRepository = templateRepository;
        }

        public override async Task<bool> IsCompleted(WaitInfrastructureDto waitDto)
        {
            var signalWaitDto = waitDto as SignalWaitDto;
            if (signalWaitDto == null)
            {
                throw new InvalidOperationException("SignalWaitMatcher: waitDto is not SignalWaitDto. Actual type: " + waitDto?.GetType().FullName);
            }

            var signal = _context.Signal;
            if (signal == null)
            {
                throw new InvalidOperationException("SignalWaitMatcher: _context.Signal is null!");
            }

            if (signalWaitDto.Status == WaitStatus.Matched)
            {
                signalWaitDto.Status = WaitStatus.Completed;

                // Retrieve explicitState
                object matchedExplicitState = null;
                if (_context.WorkflowState?.StateObject?.Locals != null)
                {
                    if (!_context.WorkflowState.StateObject.Locals.TryGetValue(signalWaitDto.StateKey.ToString(), out matchedExplicitState))
                    {
                        _context.WorkflowState.StateObject.Locals.TryGetValue(signalWaitDto.Id.ToString(), out matchedExplicitState);
                    }
                }

                // Execute AfterMatchAction — try registry by TemplateHashKey first (stable, no reflection)
                if (!string.IsNullOrWhiteSpace(signalWaitDto.TemplateHashKey))
                {
                    ExecuteAfterMatchAction(signalWaitDto.TemplateHashKey, signal.Data, matchedExplicitState);
                }

                return true;
            }

            // Validate signal identifier match
            if (!string.Equals(signalWaitDto.SignalIdentifier, signal.SignalIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[SignalWaitMatcher] Signal identifier mismatch! DTO: '{signalWaitDto.SignalIdentifier}', signal: '{signal.SignalIdentifier}'");
                throw new InvalidOperationException($"SignalWaitMatcher: Signal identifier mismatch! DTO: '{signalWaitDto.SignalIdentifier}', signal: '{signal.SignalIdentifier}'");
            }

            // Retrieve explicitState
            object explicitState = null;
            if (_context.WorkflowState?.StateObject?.Locals != null)
            {
                if (!_context.WorkflowState.StateObject.Locals.TryGetValue(signalWaitDto.StateKey.ToString(), out explicitState))
                {
                    _context.WorkflowState.StateObject.Locals.TryGetValue(signalWaitDto.Id.ToString(), out explicitState);
                }
            }
            Console.WriteLine($"[SignalWaitMatcher] explicitState type: {explicitState?.GetType().FullName}, value: {explicitState}");

            // Get or compile the match expression from cache if available
            var compiledMatch = GetOrBuildCompiledMatch(signalWaitDto);
            if (compiledMatch != null)
            {
                Console.WriteLine($"[SignalWaitMatcher] compiledMatch is not null. Evaluating...");
                // Evaluate match expression using DTO data
                bool matchResult = false;
                try
                {
                    matchResult = compiledMatch(signal.Data, explicitState, _context.WorkflowInstance);
                    Console.WriteLine($"[SignalWaitMatcher] Evaluation result: {matchResult}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SignalWaitMatcher] Evaluation threw exception: {ex}");
                }
                if (!matchResult)
                {
                    return false; // Match expression failed
                }
            }
            else
            {
                Console.WriteLine($"[SignalWaitMatcher] compiledMatch is null!");
            }

            // Execute AfterMatchAction — try registry by TemplateHashKey first (stable, no reflection)
            Console.WriteLine($"[SignalWaitMatcher] TemplateHashKey: {signalWaitDto.TemplateHashKey}");
            if (!string.IsNullOrWhiteSpace(signalWaitDto.TemplateHashKey))
            {
                try
                {
                    ExecuteAfterMatchAction(signalWaitDto.TemplateHashKey, signal.Data, explicitState);
                    Console.WriteLine($"[SignalWaitMatcher] ExecuteAfterMatchAction finished successfully.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SignalWaitMatcher] ExecuteAfterMatchAction threw exception: {ex}");
                }
            }


            // Mark this wait as completed
            signalWaitDto.Status = WaitStatus.Completed;

            return true;
        }

        private Func<object, object, object, bool> GetOrBuildCompiledMatch(SignalWaitDto dto)
        {
            if (dto.TemplateHashKey is string hashKey)
            {
                if (SignalCache.TryGetValue(hashKey, out var cached) && cached?.CompiledMatchDelegate != null)
                {
                    return cached.CompiledMatchDelegate;
                }

                // Try to load template from SQLite DB cache first
                if (_templateRepository != null)
                {
                    var dbTemplate = _templateRepository.GetTemplate(hashKey);
                    if (dbTemplate != null && dbTemplate.NormalizedMatchExpressionJson != null)
                    {
                        var normalizedExpr = _expressionSerializer.Deserialize(dbTemplate.NormalizedMatchExpressionJson);
                        var compiler = new ExpressionCompiler();
                        var compiled = compiler.CompiledMatchExpression(normalizedExpr);

                        Func<object, object, string[]>? compiledInstanceExpr = null;
                        if (dbTemplate.InstanceExactMatchExpressionJson != null)
                        {
                            compiledInstanceExpr = compiler.CompiledInstanceExactMatchExpression(
                                _expressionSerializer.Deserialize(dbTemplate.InstanceExactMatchExpressionJson));
                        }

                        var record = SignalCache.GetOrAdd(hashKey, _ => new SignalTemplateCacheRecord());
                        record.CompiledMatchDelegate = compiled;
                        record.CompiledInstanceExactMatchExpression = compiledInstanceExpr;

                        return compiled;
                    }
                }

                // No template found in DB or memory cache — expression unavailable.
                // This should not happen in normal operation; the Mapper always populates the cache first.
            }

            return null;
        }

        private string? GetAfterMatchAction(string? hashKey)
        {
            if (string.IsNullOrEmpty(hashKey)) return null;

            // Check in-memory cache first
            if (SignalCache.TryGetValue(hashKey, out var cached) && cached?.AfterMatchAction != null)
            {
                return cached.AfterMatchAction;
            }

            // Fall back to DB template
            if (_templateRepository != null)
            {
                var dbTemplate = _templateRepository.GetTemplate(hashKey);
                if (dbTemplate?.AfterMatchAction != null)
                {
                    // Warm the memory cache entry
                    var record = SignalCache.GetOrAdd(hashKey, _ => new SignalTemplateCacheRecord());
                    record.AfterMatchAction = dbTemplate.AfterMatchAction;
                    return dbTemplate.AfterMatchAction;
                }
            }

            return null;
        }

        private static readonly ConcurrentDictionary<string, Action<object, object, object>> _compiledActions = new();

        private void ExecuteAfterMatchAction(string? templateHashKey, object signalData, object explicitState)
        {
            if (string.IsNullOrEmpty(templateHashKey)) return;

            // Fast path: try the CallbackRegistry first by the stable TemplateHashKey (no reflection).
            if (_callbackRegistry.TryGet(templateHashKey, out var registeredDelegate) &&
                registeredDelegate != null)
            {
                // Always build fresh from the current delegate — do NOT cache by hash key because
                // the delegate target (e.g. the workflow container `this`) changes on every resume.
                var action = BuildRegistryInvoker(registeredDelegate.Method, registeredDelegate.Target);
                action?.Invoke(_context.WorkflowInstance, signalData, explicitState);
                return;
            }

            // Fallback: the process restarted and the in-memory CallbackRegistry is empty.
            // Look up the mapping in the SQLite template repository.
            var methodPath = GetAfterMatchAction(templateHashKey);
            if (!string.IsNullOrWhiteSpace(methodPath) && methodPath.Contains("."))
            {
                var action = _compiledActions.GetOrAdd(methodPath, path =>
                {
                    var method = Helpers.MethodResolver.ResolveMethod(path);
                    if (method == null) return null;
                    return Helpers.MethodResolver.BuildMethodInvoker(method);
                });

                action?.Invoke(_context.WorkflowInstance, signalData, explicitState);
            }
        }

        /// <summary>
        /// Builds an invoker for a live delegate from the <see cref="ICallbackRegistry"/>.
        /// Uses <c>Expression.Constant(target)</c> when the method belongs to a helper type
        /// (e.g. <c>StatefulAfterMatchInvoker</c>), so the captured closure is invoked.
        /// Uses <c>Expression.Convert(instanceParam, DeclaringType)</c> when the method
        /// belongs to a <see cref="WorkflowContainer"/> subclass, so the live deserialized
        /// instance is used instead of the stale first-run instance.
        /// </summary>
        private static Action<object, object, object> BuildRegistryInvoker(
            System.Reflection.MethodInfo method, object? target)
        {
            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var signalParam   = Expression.Parameter(typeof(object), "signal");
            var stateParam    = Expression.Parameter(typeof(object), "state");

            var parameters = method.GetParameters();

            Expression targetExpr;
            if (method.IsStatic)
            {
                targetExpr = null!;
            }
            else if (target != null && !(target is WorkflowContainer))
            {
                // Helper object (e.g. StatefulAfterMatchInvoker) — bind directly to the captured instance.
                targetExpr = Expression.Constant(target);
            }
            else
            {
                // WorkflowContainer method — use the live deserialized workflow instance.
                targetExpr = Expression.Convert(instanceParam, method.DeclaringType!);
            }

            Expression call;
            if (parameters.Length == 0)
            {
                call = Expression.Call(targetExpr, method);
            }
            else if (parameters.Length == 1)
            {
                call = Expression.Call(targetExpr, method,
                    Expression.Convert(signalParam, parameters[0].ParameterType));
            }
            else if (parameters.Length == 2)
            {
                var convertStateMethod = typeof(StateConverter).GetMethod(
                    nameof(StateConverter.ConvertState),
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                call = Expression.Call(targetExpr, method,
                    Expression.Convert(signalParam, parameters[0].ParameterType),
                    Expression.Convert(
                        Expression.Call(convertStateMethod!, stateParam, Expression.Constant(parameters[1].ParameterType)),
                        parameters[1].ParameterType));
            }
            else
            {
                return null!; // unsupported — skip silently
            }

            return Expression.Lambda<Action<object, object, object>>(call, instanceParam, signalParam, stateParam)
                             .CompileFast();
        }

        /// <summary>Builds an <c>Action&lt;object,object,object&gt;</c> invoker from a live delegate.</summary>
        private static Action<object, object, object> BuildDelegateInvoker(
            System.Reflection.MethodInfo method, object? target)
        {
            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var signalParam   = Expression.Parameter(typeof(object), "signal");
            var stateParam    = Expression.Parameter(typeof(object), "state");

            var parameters = method.GetParameters();
            Expression targetExpr = method.IsStatic
                ? null!
                : target != null
                    ? Expression.Constant(target)          // bound closure — use captured target directly
                    : Expression.Convert(instanceParam, method.DeclaringType!);

            Expression call;
            if (parameters.Length == 0)
                call = Expression.Call(targetExpr, method);
            else if (parameters.Length == 1)
                call = Expression.Call(targetExpr, method, Expression.Convert(signalParam, parameters[0].ParameterType));
            else
                return null!; // unsupported — skip silently

            return Expression.Lambda<Action<object, object, object>>(call, instanceParam, signalParam, stateParam)
                             .CompileFast();
        }


    }
}
