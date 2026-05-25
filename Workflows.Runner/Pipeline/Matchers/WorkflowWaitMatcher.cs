using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Runner.Pipeline.Matchers
{
    /// <summary>
    /// Base class for matchers that validate incoming events (signal, timer, command result) against 
    /// wait constraints before advancing the state machine. Returns false if conditions fail or match 
    /// partially, halting execution immediately.
    /// </summary>
    internal abstract class WorkflowWaitMatcher
    {
        /// <summary>
        /// Matches the incoming event against the wait. Returns true if the match succeeds
        /// and the workflow should proceed; false if matching fails or is incomplete.
        /// Context is injected via DI (scoped).
        /// </summary>
        public abstract Task<bool> MatchAsync(WaitInfrastructureDto waitDto);

        /// <summary>
        /// Helper method to propagate matching to parent wait (e.g., GroupWait or SubWorkflowWait).
        /// After a child wait matches successfully, this checks if the parent condition is also met.
        /// </summary>
        protected async Task<bool> MatchParentAsync(
            Guid parentWaitId,
            WorkflowExecutionContext context,
            MatcherFactory matcherFactory)
        {
            // Find parent wait DTO from the workflow state
            var parentWaitDto = FindWaitById(context.WorkflowState.Waits, parentWaitId);
            if (parentWaitDto == null)
            {
                // Parent not found - this shouldn't happen but assume success
                return true;
            }

            // Get the appropriate matcher for the parent wait
            var parentMatcher = matcherFactory.GetMatcher(parentWaitDto);

            // Recursively match parent
            bool parentMatches = await parentMatcher.MatchAsync(parentWaitDto);

            return parentMatches;
        }

        /// <summary>
        /// Recursively finds a wait DTO by ID in the wait tree.
        /// </summary>
        protected static WaitInfrastructureDto FindWaitById(System.Collections.Generic.IEnumerable<WaitInfrastructureDto> waits, Guid id)
        {
            if (waits == null) return null;

            foreach (var wait in waits)
            {
                if (wait == null) continue;
                if (wait.Id == id) return wait;

                var child = FindWaitById(wait.ChildWaits, id);
                if (child != null) return child;
            }

            return null;
        }
    }
}
