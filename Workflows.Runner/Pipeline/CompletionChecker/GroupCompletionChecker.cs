using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using FastExpressionCompiler;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Persistence;
using Workflows.Definition;
using Workflows.Primitives;


namespace Workflows.Runner.Pipeline.CompletionChecker
{
    /// <summary>
    /// Evaluates compound boolean status trees for GroupWait (e.g., MatchAll, MatchAny).
    /// This matcher is ONLY called via parent propagation from child matchers, never directly by the runner.
    /// If fulfilled, handles downward pruning of remaining branches.
    /// </summary>
    internal class GroupCompletionChecker : WaitCompletionChecker
    {
        private readonly WorkflowExecutionContext _context;
        private readonly ICallbackRegistry _callbackRegistry;
        private readonly ITemplateRepository? _templateRepository;
        private static readonly ConcurrentDictionary<string, Func<object, object, bool>> _compiledFilters = new();

        public GroupCompletionChecker(
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
            // NOTE: This matcher is only invoked via MatchParentAsync from child matchers.
            var groupWaitDto = waitDto as GroupWaitDto;
            if (groupWaitDto == null)
            {
                throw new InvalidOperationException(
                    "GroupWaitMatcher can only be called via parent propagation with a GroupWaitDto.");
            }

            // Get all child waits from DTO
            if (groupWaitDto.ChildWaits == null || !groupWaitDto.ChildWaits.Any())
            {
                // No children means group is complete
                groupWaitDto.Status = WaitStatus.Completed;

                return true;
            }

            // Count completed children
            var completedChildren = groupWaitDto.ChildWaits.Count(child => child.Status == WaitStatus.Completed || child.Status == WaitStatus.Matched);
            var totalChildren = groupWaitDto.ChildWaits.Count;

            bool groupMatches = false;

            // Evaluate based on wait type
            switch (groupWaitDto.WaitType)
            {
                case WaitType.GroupWaitAll: // MatchAll
                    groupMatches = completedChildren == totalChildren;
                    break;

                case WaitType.GroupWaitFirst: // MatchAny / MatchFirst
                    groupMatches = completedChildren >= 1;
                    if (groupMatches)
                    {
                        // Downward pruning: mark remaining children as cancelled
                        PruneRemainingChildren(groupWaitDto, _context);
                    }
                    break;

                case WaitType.GroupWaitWithExpression: // MatchIf with custom expression
                    groupMatches = EvaluateGroupFilter(groupWaitDto);
                    if (groupMatches)
                    {
                        PruneRemainingChildren(groupWaitDto, _context);
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported GroupWait type: {groupWaitDto.WaitType}");
            }

            if (groupMatches)
            {
                // Mark group as completed
                groupWaitDto.Status = WaitStatus.Completed;
            }

            return groupMatches;
        }

        private bool EvaluateGroupFilter(GroupWaitDto groupWaitDto)
        {
            if (string.IsNullOrWhiteSpace(groupWaitDto.MatchFuncName))
            {
                return false;
            }

            // Retrieve explicitState
            object explicitState = null;
            if (_context.WorkflowState?.StateObject?.Locals != null)
            {
                if (!_context.WorkflowState.StateObject.Locals.TryGetValue(groupWaitDto.StateKey.ToString(), out explicitState))
                {
                    _context.WorkflowState.StateObject.Locals.TryGetValue(groupWaitDto.Id.ToString(), out explicitState);
                }
            }

            var filter = _compiledFilters.GetOrAdd(groupWaitDto.MatchFuncName, path =>
            {
                // Try CallbackRegistry first
                MethodInfo method = null;
                object target = null;

                if (_callbackRegistry.TryGet(path, out var registeredDelegate) && registeredDelegate != null)
                {
                    method = registeredDelegate.Method;
                    target = registeredDelegate.Target;
                }
                else
                {
                    // Fallback: look up in SQLite template repository
                    if (_templateRepository != null)
                    {
                        var dbTemplate = _templateRepository.GetTemplate(path);
                        if (dbTemplate != null && !string.IsNullOrWhiteSpace(dbTemplate.AfterMatchAction))
                        {
                            method = Helpers.MethodResolver.ResolveMethod(dbTemplate.AfterMatchAction);
                        }
                    }
                }

                if (method == null) return null;

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var stateParam = Expression.Parameter(typeof(object), "state");

                var parameters = method.GetParameters();
                
                var declaringType = method.DeclaringType;
                if (target == null && !method.IsStatic && declaringType != null && !typeof(WorkflowContainer).IsAssignableFrom(declaringType))
                {
                    return (instance, state) =>
                    {
                        var targetObj = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(declaringType);
                        var containerField = System.Attribute.IsDefined(declaringType, typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute))
                            || declaringType.Name.Contains("<")
                            ? System.Linq.Enumerable.FirstOrDefault(declaringType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                                f => typeof(WorkflowContainer).IsAssignableFrom(f.FieldType))
                            : null;

                        if (containerField != null)
                        {
                            containerField.SetValue(targetObj, instance);
                        }

                        var args = new object[parameters.Length];
                        if (parameters.Length > 0)
                        {
                            args[0] = StateConverter.ConvertState(state, parameters[0].ParameterType);
                        }

                        return (bool)method.Invoke(targetObj, args)!;
                    };
                }

                Expression targetExpr;
                if (method.IsStatic)
                {
                    targetExpr = null;
                }
                else if (target != null && !(target is WorkflowContainer))
                {
                    targetExpr = Expression.Constant(target);
                }
                else
                {
                    targetExpr = Expression.Convert(instanceParam, method.DeclaringType);
                }

                Expression call;
                if (parameters.Length == 0)
                {
                    // Func<bool> - stateless
                    call = Expression.Call(targetExpr, method);
                }
                else if (parameters.Length == 1)
                {
                    // Func<TState, bool> - stateful
                    var convertStateMethod = typeof(StateConverter).GetMethod(
                        nameof(StateConverter.ConvertState), 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    call = Expression.Call(targetExpr, method,
                        Expression.Convert(
                            Expression.Call(convertStateMethod!, stateParam, Expression.Constant(parameters[0].ParameterType)),
                            parameters[0].ParameterType));
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported GroupMatchFilter method signature: {method}");
                }

                var lambda = Expression.Lambda<Func<object, object, bool>>(call, instanceParam, stateParam);
                return lambda.CompileFast();
            });

            if (filter == null)
            {
                return false;
            }

            try
            {
                return filter(_context.WorkflowInstance, explicitState);
            }
            catch
            {
                return false;
            }
        }

        private void PruneRemainingChildren(GroupWaitDto groupWaitDto, WorkflowExecutionContext context)
        {
            // Mark all non-completed children for removal
            var childrenToPrune = groupWaitDto.ChildWaits
                .Where(child => child.Status == WaitStatus.Waiting)
                .ToList();

            foreach (var child in childrenToPrune)
            {
                child.Status = WaitStatus.Canceled;
                // Mark as consumed so they get removed from persistence
                context.ConsumedWaitsIds.Add(child.Id);

                // Recursively prune nested children
                if (child.ChildWaits != null && child.ChildWaits.Any())
                {
                    PruneChildrenRecursive(child, context);
                }
            }
        }

        private void PruneChildrenRecursive(WaitInfrastructureDto wait, WorkflowExecutionContext context)
        {
            if (wait.ChildWaits == null) return;

            foreach (var child in wait.ChildWaits)
            {
                if (child.Status == WaitStatus.Waiting)
                {
                    child.Status = WaitStatus.Canceled;
                    context.ConsumedWaitsIds.Add(child.Id);
                    PruneChildrenRecursive(child, context);
                }
            }
        }
    }
}

