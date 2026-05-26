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
using Workflows.Runner.Cache;
using Workflows.Runner.ExpressionTransformers;
using IExpressionSerializer = Workflows.Abstraction.Helpers.IExpressionSerializer;
using ExpressionCompiler = Workflows.Runner.ExpressionTransformers.ExpressionCompiler;

namespace Workflows.Runner.Pipeline.Matchers
{
    /// <summary>
    /// Matches incoming signal events against SignalWait constraints.
    /// Works purely with DTOs - no Wait object conversion needed.
    /// </summary>
    internal class SignalWaitMatcher : WorkflowWaitMatcher
    {
        internal static readonly ConcurrentDictionary<string, SignalTemplateCacheRecord> SignalCache = new();

        private readonly IWorkflowRegistry _workflowRegistry;
        private readonly WorkflowExecutionContext _context;
        private readonly MatcherFactory _matcherFactory;
        private readonly IExpressionSerializer _expressionSerializer;
        private readonly IDelegateSerializer _delegateSerializer;
        private readonly MatchExpressionTransformer _matchExpressionTransformer;
        private readonly ITemplateRepository? _templateRepository;

        public SignalWaitMatcher(
            IWorkflowRegistry workflowRegistry, 
            WorkflowExecutionContext context,
            MatcherFactory matcherFactory,
            IExpressionSerializer expressionSerializer,
            IDelegateSerializer delegateSerializer,
            MatchExpressionTransformer matchExpressionTransformer,
            ITemplateRepository? templateRepository = null)
        {
            _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _matcherFactory = matcherFactory ?? throw new ArgumentNullException(nameof(matcherFactory));
            _expressionSerializer = expressionSerializer ?? throw new ArgumentNullException(nameof(expressionSerializer));
            _delegateSerializer = delegateSerializer ?? throw new ArgumentNullException(nameof(delegateSerializer));
            _matchExpressionTransformer = matchExpressionTransformer ?? throw new ArgumentNullException(nameof(matchExpressionTransformer));
            _templateRepository = templateRepository;
        }

        public override async Task<bool> MatchAsync(WaitInfrastructureDto waitDto)
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

            // Validate signal identifier match
            if (!string.Equals(signalWaitDto.SignalIdentifier, signal.SignalIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"SignalWaitMatcher: Signal identifier mismatch! DTO: '{signalWaitDto.SignalIdentifier}', signal: '{signal.SignalIdentifier}'");
            }

            // Retrieve explicitState
            object explicitState = null;
            if (_context.WorkflowState?.StateObject?.WaitStatesObjects != null)
            {
                if (!_context.WorkflowState.StateObject.WaitStatesObjects.TryGetValue(signalWaitDto.StateKey, out explicitState))
                {
                    _context.WorkflowState.StateObject.WaitStatesObjects.TryGetValue(signalWaitDto.Id, out explicitState);
                }
            }

            // Get or compile the match expression from cache if available
            var compiledMatch = GetOrBuildCompiledMatch(signalWaitDto);
            if (compiledMatch != null)
            {
                // Evaluate match expression using DTO data
                bool matchResult = compiledMatch(signal.Data, explicitState, _context.WorkflowInstance);
                if (!matchResult)
                {
                    return false; // Match expression failed
                }
            }

            // Execute AfterMatchAction — stored on the DTO (instance-specific, captures closure state)
            var afterMatchAction = signalWaitDto.AfterMatchAction;
            if (!string.IsNullOrWhiteSpace(afterMatchAction))
            {
                ExecuteAfterMatchAction(afterMatchAction, signal.Data, explicitState);
            }


            // Mark this wait as completed
            signalWaitDto.Status = WaitStatus.Completed;

            // Propagate matching to parent wait (e.g., GroupWait or SubWorkflowWait) if present
            if (signalWaitDto.ParentWaitId.HasValue)
            {
                return await MatchParentAsync(signalWaitDto.ParentWaitId.Value, _context, _matcherFactory);
            }

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

        private void ExecuteAfterMatchAction(string afterMatchActionPath, object signalData, object explicitState)
        {
            var action = _compiledActions.GetOrAdd(afterMatchActionPath, path =>
            {
                var method = _delegateSerializer.Deserialize(path);
                if (method == null) return null;

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var signalParam = Expression.Parameter(typeof(object), "signal");
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
                    call = Expression.Call(targetExpr, method, Expression.Convert(signalParam, parameters[0].ParameterType));
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
                    throw new InvalidOperationException($"Unsupported AfterMatchAction method signature: {method}");
                }

                var lambda = Expression.Lambda<Action<object, object, object>>(call, instanceParam, signalParam, stateParam);
                return lambda.CompileFast();
            });

            action?.Invoke(_context.WorkflowInstance, signalData, explicitState);
        }
    }
}
