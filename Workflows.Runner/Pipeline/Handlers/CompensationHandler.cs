using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Workflows.Definition;
using Workflows.Runner.Cache;

namespace Workflows.Runner.Pipeline.Handlers
{
    /// <summary>
    /// Handles CompensationWait objects.
    /// Queries history, sorts past operations using Last-In, First-Out (LIFO) sequencing,
    /// invokes compiled undo delegates, and flags nodes as compensated. Returns true to continue execution.
    /// </summary>
    internal class CompensationHandler : WorkflowWaitHandler
    {
        private readonly WorkflowTemplateCache _templateCache;

        public CompensationHandler(WorkflowTemplateCache templateCache)
        {
            _templateCache = templateCache ?? throw new ArgumentNullException(nameof(templateCache));
        }

        public override async Task<bool> HandleAsync(Wait yieldedWait, WorkflowExecutionContext context)
        {
            var compensationWait = yieldedWait as CompensationWait;
            if (compensationWait == null)
            {
                throw new InvalidOperationException("CompensationHandler requires a CompensationWait.");
            }

            // Query history from context.ActiveState
            var commandHistory = BuildCommandHistory(context.ActiveState);

            // Filter to only commands that match the compensation token and are not already compensated
            var commandsToCompensate = commandHistory
                .Where(entry => !entry.IsCompensated && 
                               (string.IsNullOrEmpty(compensationWait.Token) || 
                                entry.Tokens.Contains(compensationWait.Token)))
                .OrderByDescending(entry => entry.ExecutionOrder) // LIFO
                .ToList();

            // Execute compensation for each command
            foreach (var entry in commandsToCompensate)
            {
                if (entry.CompensationAction != null)
                {
                    try
                    {
                        // Invoke compensation action
                        await InvokeCompensationActionAsync(entry.CompensationAction, entry.Result, entry.ExplicitState);

                        // Mark as compensated
                        entry.IsCompensated = true;
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue with other compensations
                        if (context.WorkflowInstance != null)
                        {
                            await context.WorkflowInstance.OnError(
                                $"Compensation failed for command {entry.CommandType}: {ex.Message}", ex);
                        }
                    }
                }
                else
                {
                    // No compensation action defined, just mark as compensated
                    entry.IsCompensated = true;
                }
            }

            // Update command history in state
            UpdateCommandHistoryInState(context.ActiveState, commandHistory);

            // Return true - active wait, continue execution loop
            return true;
        }

        private List<CommandHistoryEntry> BuildCommandHistory(Workflows.Abstraction.DTOs.WorkflowStateObject stateObject)
        {
            var commandHistoryKey = new Guid("00000000-0000-0000-0000-000000000001");

            if (stateObject.StateMachinesObjects?.TryGetValue(commandHistoryKey, out var historyObj) == true)
            {
                return historyObj as List<CommandHistoryEntry> ?? new List<CommandHistoryEntry>();
            }

            return new List<CommandHistoryEntry>();
        }

        private void UpdateCommandHistoryInState(
            Workflows.Abstraction.DTOs.WorkflowStateObject stateObject,
            List<CommandHistoryEntry> commandHistory)
        {
            stateObject.StateMachinesObjects ??= new Dictionary<Guid, object>();
            var commandHistoryKey = new Guid("00000000-0000-0000-0000-000000000001");
            stateObject.StateMachinesObjects[commandHistoryKey] = commandHistory;
        }

        private async Task InvokeCompensationActionAsync(object action, object result, object explicitState)
        {
            // Use template cache to get the compiled invoker
            var invoker = _templateCache.GetOrAddCompensationInvoker(action.GetType());
            if (invoker == null)
            {
                throw new InvalidOperationException("CompensationAction signature is not supported.");
            }

            await invoker(action, result, explicitState);
        }
    }
}
