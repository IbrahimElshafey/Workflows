using System;
using System.Collections.Generic;
using Workflows.Definition;
using Workflows.Primitives;

namespace Workflows.Definition
{
    /// <summary>
    /// Base class for migration classes.
    /// Exposes the same Wait factory DSL as WorkflowContainer so that
    /// MigrateActiveWait uses identical syntax — no new vocabulary.
    /// </summary>
    public abstract class MigrationContainer
    {
        // ── The same factory signatures as WorkflowContainer ──────────────────

        protected SignalBuilder<TPayload> WaitSignal<TPayload>(string signalIdentifier, string name)
        {
            var newSignalWait = new SignalWait<TPayload>
            {
                SignalIdentifier = signalIdentifier,
                WaitName = name,
                WorkflowContainer = _context,
                WaitType = WaitType.SignalWait
            };
            return new SignalBuilder<TPayload>(newSignalWait);
        }

        protected TimeWait WaitDelay(TimeSpan delay, string name)
            => new TimeWait
            {
                WaitName = name,
                TimeToWait = delay,
                UniqueMatchId = Guid.NewGuid().ToString(),
                WaitType = WaitType.SignalWait,
                WorkflowContainer = _context
            };

        protected GroupWait WaitGroup(Wait[] children, string name)
            => new GroupWait
            {
                ChildWaits = new List<Wait>(children),
                WaitName = name,
                WaitType = WaitType.GroupWaitAll,
                WorkflowContainer = _context
            };

        /// <param name="runner">Optional — omit to let the engine resolve by name from V2 manifest.</param>
        protected SubWorkflowWait WaitSubWorkflow(string name, IAsyncEnumerable<Wait>? runner = null)
            => new SubWorkflowWait
            {
                WaitName = name,
                Runner = runner!,
                WaitType = WaitType.SubWorkflowWait,
                WorkflowContainer = _context
            };

        // ── Migration-only helpers ────────────────────────────────────────────

        /// <summary>
        /// Marks that this wait should be reconstructed from the V2 CFG by name.
        /// The engine resolves the StateAfterWait index before writing to DB.
        /// </summary>
        protected Wait RecreateWait(string waitName)
            => new PlaceholderWait { WaitName = waitName };

        /// <summary>
        /// Same as RecreateWait but scoped to a specific sub-workflow method.
        /// </summary>
        protected Wait SubWorkflow_RecreateWait(string methodFullPath, string waitName)
            => new PlaceholderSubWorkflowWait { MethodFullPath = methodFullPath, WaitName = waitName };

        /// <summary>
        /// Schedules a command to be dispatched AFTER the migration transaction commits.
        /// Required when jumping directly to a checkpoint that would normally have fired a command.
        /// </summary>
        protected void ScheduleCommand(object command)
            => _scheduledCommands.Add(command);

        // Internal — used by WorkflowMigrationExecutor
        internal WorkflowContainer? _context;
        internal readonly List<object> _scheduledCommands = new();
    }

    // Internal sentinel types — resolved by the executor before DB write
    internal sealed class PlaceholderWait : Wait
    {
        public PlaceholderWait() { WaitType = WaitType.Placeholder; }
    }
    internal sealed class PlaceholderSubWorkflowWait : Wait
    {
        public string MethodFullPath { get; set; } = "";
        public PlaceholderSubWorkflowWait() { WaitType = WaitType.PlaceholderSubWorkflow; }
    }
}
