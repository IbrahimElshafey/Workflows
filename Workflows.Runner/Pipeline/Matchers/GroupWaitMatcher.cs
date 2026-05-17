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
    /// If fulfilled, handles downward pruning of remaining branches.
    /// </summary>
    internal class GroupWaitMatcher : WorkflowWaitMatcher
    {
        public override Task<bool> MatchAsync(WorkflowExecutionContext context)
        {
            var groupWaitDto = context.TriggeringWaitDto as GroupWaitDto;
            if (groupWaitDto == null)
            {
                throw new InvalidOperationException("GroupWaitMatcher requires a GroupWaitDto.");
            }

            var groupWait = context.TriggeringWait as Definition.GroupWait;
            if (groupWait == null)
            {
                throw new InvalidOperationException("Triggering wait could not be mapped to GroupWait.");
            }

            // Get all child waits from DTO
            if (groupWaitDto.ChildWaits == null || !groupWaitDto.ChildWaits.Any())
            {
                // No children means group is complete
                return Task.FromResult(true);
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
                        PruneRemainingChildren(groupWaitDto, context);
                    }
                    break;

                case WaitType.GroupWaitWithExpression: // MatchIf with custom expression
                    if (groupWait.GroupMatchFilter != null)
                    {
                        try
                        {
                            groupMatches = groupWait.GroupMatchFilter();
                            if (groupMatches)
                            {
                                // Prune any remaining incomplete children
                                PruneRemainingChildren(groupWaitDto, context);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log error but don't throw - treat as false
                            if (context.WorkflowInstance != null)
                            {
                                context.WorkflowInstance.OnError(
                                    $"GroupMatchFilter evaluation failed for wait {groupWait.WaitName}: {ex.Message}", ex).Wait();
                            }
                            groupMatches = false;
                        }
                    }
                    else
                    {
                        // No filter means all children must complete
                        groupMatches = completedChildren == totalChildren;
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported GroupWait type: {groupWaitDto.WaitType}");
            }

            return Task.FromResult(groupMatches);
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

