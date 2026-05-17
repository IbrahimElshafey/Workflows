using System;
using System.Linq;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
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

        public GroupWaitMatcher(WorkflowExecutionContext context, MatcherFactory matcherFactory)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _matcherFactory = matcherFactory ?? throw new ArgumentNullException(nameof(matcherFactory));
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
                    // TODO: For custom filters, we need to store and evaluate the filter from DTO
                    // For now, treat as MatchAll
                    groupMatches = completedChildren == totalChildren;
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

