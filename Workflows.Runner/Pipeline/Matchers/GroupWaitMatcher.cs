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
using Workflows.Primitives;


namespace Workflows.Runner.Pipeline.Matchers
{
    /// <summary>
    /// Evaluates compound boolean status trees for GroupWait (e.g., MatchAll, MatchAny).
    /// This matcher is ONLY called via parent propagation from child matchers, never directly by the runner.
    /// If fulfilled, handles downward pruning of remaining branches.
    /// </summary>
    internal class GroupWaitMatcher : WorkflowWaitMatcher
    {
        private readonly WorkflowExecutionContext _context;
        private readonly MatcherFactory _matcherFactory;
        private readonly IDelegateSerializer _delegateSerializer;
        private static readonly ConcurrentDictionary<string, Func<object, object, bool>> _compiledFilters = new();

        public GroupWaitMatcher(
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

                // Propagate to parent if present
                if (groupWaitDto.ParentWaitId.HasValue)
                {
                    return await MatchParentAsync(groupWaitDto.ParentWaitId.Value, _context, _matcherFactory);
                }

                return true;
            }

            // Count completed children
            var completedChildren = groupWaitDto.ChildWaits.Count(child => child.Status == WaitStatus.Completed);
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

                // Propagate to parent if present
                if (groupWaitDto.ParentWaitId.HasValue)
                {
                    return await MatchParentAsync(groupWaitDto.ParentWaitId.Value, _context, _matcherFactory);
                }
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
            if (_context.WorkflowState?.StateObject?.WaitStatesObjects != null)
            {
                if (!_context.WorkflowState.StateObject.WaitStatesObjects.TryGetValue(groupWaitDto.StateKey, out explicitState))
                {
                    _context.WorkflowState.StateObject.WaitStatesObjects.TryGetValue(groupWaitDto.Id, out explicitState);
                }
            }

            var filter = _compiledFilters.GetOrAdd(groupWaitDto.MatchFuncName, path =>
            {
                var method = _delegateSerializer.Deserialize(path);
                if (method == null) return null;

                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var stateParam = Expression.Parameter(typeof(object), "state");

                var parameters = method.GetParameters();
                
                Expression targetExpr;
                if (method.IsStatic)
                {
                    targetExpr = null;
                }
                else if (method.DeclaringType != null && method.DeclaringType.Name.Contains("<>c"))
                {
                    var singletonField = method.DeclaringType.GetField("<>9", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    object targetObj = null;
                    if (singletonField != null)
                    {
                        targetObj = singletonField.GetValue(null);
                    }
                    if (targetObj == null)
                    {
                        targetObj = Activator.CreateInstance(method.DeclaringType);
                    }

                    targetExpr = Expression.Constant(targetObj, method.DeclaringType);
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
                    context.ConsumedWaitsIds.Add(child.Id);
                    PruneChildrenRecursive(child, context);
                }
            }
        }
    }
}

