using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline
{
    /// <summary>
    /// Handles cancellation logic and triggers attached OnCancel callbacks.
    /// Executed at the tail end of evaluation and generation loops.
    /// Matches active trees against the CancellationHistory.
    /// </summary>
    internal class CancelHandler
    {
        /// <summary>
        /// Processes cancellations and executes developer-defined OnCancel or OnFailure callbacks
        /// before pruning the sub-tree and fast-forwarding the execution index.
        /// </summary>
        public async Task ProcessCancellationsWithCallbacksAsync(WorkflowExecutionContext context)
        {
            if (context.WorkflowState.CancellationHistory == null || !context.WorkflowState.CancellationHistory.Any())
            {
                return;
            }

            var cancelledTokens = context.WorkflowState.CancellationHistory.GetCancelledTokens();

            // Check if the currently advancing wait needs to be cancelled
            // This is typically checked in the main loop after yielding a wait
            // For now, this is a placeholder for future cancellation tree pruning logic

            await Task.CompletedTask;
        }

        /// <summary>
        /// Checks if a wait should be cancelled based on its tokens.
        /// </summary>
        public bool IsWaitCancelled(Wait wait, HashSet<string> cancelledTokens)
        {
            if (cancelledTokens == null || !cancelledTokens.Any())
            {
                return false;
            }

            // Check if wait has cancel tokens that match
            if (wait is IPassiveWait passiveWait && passiveWait.CancelTokens != null)
            {
                return passiveWait.CancelTokens.Intersect(cancelledTokens).Any();
            }

            return false;
        }

        /// <summary>
        /// Invokes the cancel action callback for a wait if present.
        /// </summary>
        public async Task InvokeCancelActionAsync(Wait wait)
        {
            if (wait.CancelAction != null)
            {
                try
                {
                    await wait.CancelAction();
                }
                catch (Exception ex)
                {
                    // Log but don't throw - cancellation callbacks should not block workflow
                    if (wait.WorkflowContainer != null)
                    {
                        await wait.WorkflowContainer.OnError($"Cancel action failed for wait {wait.WaitName}: {ex.Message}", ex);
                    }
                }
            }
        }
    }
}
