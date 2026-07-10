using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Runner.Pipeline.CompletionChecker
{
    /// <summary>
    /// Base class for completion checkers that evaluate whether a specific wait node is satisfied.
    /// Each checker only evaluates its own node — parent traversal is handled by the runner.
    /// </summary>
    internal abstract class WaitCompletionChecker
    {
        /// <summary>
        /// Checks whether this specific wait node is completed.
        /// Returns true if the wait is satisfied; false otherwise.
        /// Does NOT traverse to parent nodes — the runner handles parent bubbling.
        /// </summary>
        public abstract Task<bool> IsCompleted(WaitInfrastructureDto waitDto);

        /// <summary>
        /// Recursively finds a wait DTO by ID in the wait tree.
        /// </summary>
        protected static WaitInfrastructureDto FindWaitById(System.Collections.Generic.IEnumerable<WaitInfrastructureDto> waits, string id)
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
