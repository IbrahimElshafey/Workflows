using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Processors
{
    /// <summary>
    /// Handles cancellation logic and triggers attached OnCancel callbacks.
    /// Executed at the tail end of evaluation and generation loops.
    /// Matches active trees against the CancellationHistory.
    /// </summary>
    internal class CancelProcessor
    {
        private static readonly ActionInvokerCache _invokerCache = new();

        /// <summary>
        /// Checks if a yielded wait should be cancelled and skips it if so.
        /// Returns true if wait was cancelled and execution should continue to next wait.
        /// </summary>
        public async Task<bool> CheckAndSkipCancelledWaitAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            if (yieldedWait == null) return false;

            var cancelledTokens = context.WorkflowState.CancellationHistory?.GetCancelledTokens();
            if (cancelledTokens == null || !cancelledTokens.Any())
            {
                return false;
            }

            bool isCancelled = IsWaitCancelled(yieldedWait, cancelledTokens);
            if (isCancelled)
            {
                // Invoke cancel callback
                await InvokeCancelActionAsync(yieldedWait);

                // Wait is cancelled - signal to skip it and continue loop
                return true;
            }

            return false;
        }

        /// <summary>
        /// Processes cancellations and executes developer-defined OnCancel or OnFailure callbacks
        /// before pruning the sub-tree and fast-forwarding the execution index.
        /// Called at the end of each execution loop iteration.
        /// </summary>
        public async Task ProcessCancellationsWithCallbacksAsync(WorkflowExecutionContext context)
        {
            if (context.WorkflowState.CancellationHistory == null || !context.WorkflowState.CancellationHistory.Any())
            {
                return;
            }

            var cancelledTokens = context.WorkflowState.CancellationHistory.GetCancelledTokens();

            // Prune any waits that match cancelled tokens before they get persisted
            var waitsToRemove = context.WorkflowState.Waits
                .Where(waitDto => ShouldWaitBeCancelled(waitDto, cancelledTokens))
                .ToList();

            foreach (var waitDto in waitsToRemove)
            {
                // Mark as consumed so it doesn't get persisted
                context.ConsumedWaitsIds.Add(waitDto.Id);
                context.WorkflowState.Waits.Remove(waitDto);

                // Recursively prune children
                PruneChildWaits(waitDto, context, cancelledTokens);
            }
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
            if (wait.CancelTokens != null)
            {
                return wait.CancelTokens.Intersect(cancelledTokens).Any();
            }

            return false;
        }

        private bool ShouldWaitBeCancelled(Abstraction.DTOs.Waits.WaitInfrastructureDto waitDto, HashSet<string> cancelledTokens)
        {
            if (waitDto == null || cancelledTokens == null || !cancelledTokens.Any())
            {
                return false;
            }

            // Check signal wait
            if (waitDto is Abstraction.DTOs.Waits.SignalWaitDto signalWait && signalWait.CancelTokens != null)
            {
                return signalWait.CancelTokens.Intersect(cancelledTokens).Any();
            }

            // Check time wait
            if (waitDto is Abstraction.DTOs.Waits.TimeWaitDto timeWait && timeWait.CancelTokens != null)
            {
                return timeWait.CancelTokens.Intersect(cancelledTokens).Any();
            }

            // Check command wait
            if (waitDto is Abstraction.DTOs.Waits.CommandWaitDto commandWait && commandWait.CancelTokens != null)
            {
                return commandWait.CancelTokens.Intersect(cancelledTokens).Any();
            }

            // Check group wait
            if (waitDto is Abstraction.DTOs.Waits.GroupWaitDto groupWait && groupWait.CancelTokens != null)
            {
                return groupWait.CancelTokens.Intersect(cancelledTokens).Any();
            }

            // Check sub-workflow wait
            if (waitDto is Abstraction.DTOs.Waits.SubWorkflowWaitDto subWorkflow && subWorkflow.CancelTokens != null)
            {
                return subWorkflow.CancelTokens.Intersect(cancelledTokens).Any();
            }

            return false;
        }

        private void PruneChildWaits(
            Abstraction.DTOs.Waits.WaitInfrastructureDto waitDto, 
            WorkflowExecutionContext context,
            HashSet<string> cancelledTokens)
        {
            if (waitDto.ChildWaits == null || !waitDto.ChildWaits.Any())
            {
                return;
            }

            foreach (var child in waitDto.ChildWaits.ToList())
            {
                context.ConsumedWaitsIds.Add(child.Id);
                PruneChildWaits(child, context, cancelledTokens);
            }
        }

        /// <summary>
        /// Invokes the cancel action callback for a wait if present.
        /// </summary>
        public async Task InvokeCancelActionAsync(Wait wait)
        {
            if (wait.CancelAction == null) return;

            try
            {
                //todo: to fix
                switch (wait.CancelAction)
                {
                    case Func<ValueTask> asyncAction:
                        await asyncAction();
                        break;
                    case Func<object, ValueTask> asyncActionWithState:
                        await asyncActionWithState(wait);
                        break;
                    case Action action:
                        action();
                        break;
                    case Action<object> actionWithState:
                        actionWithState(wait);
                        break;
                }
            }
            catch (Exception ex)
            {
                if (wait.WorkflowContainer != null)
                {
                    await wait.WorkflowContainer.OnError($"Cancel action failed for wait {wait.WaitName}: {ex.Message}", ex);
                }
            }
        }
    }
}
