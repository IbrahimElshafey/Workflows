using System;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Orchestrator;

namespace Workflows.Definition
{
    /// <summary>
    /// Base class for all strongly-typed version migration classes. Inherit this, decorate with
    /// [WorkflowMigration], and register with services.AddWorkflowMigration&lt;&gt;().
    /// </summary>
    public abstract class WorkflowMigration<TOld, TNew> : MigrationContainer
        where TOld : WorkflowStateWrapper
        where TNew : WorkflowStateWrapper
    {
        /// <summary>
        /// Gets or sets the migration strategy to execute (InPlace or CancelAndRespawn).
        /// </summary>
        public MigrationStrategy Strategy { get; set; } = MigrationStrategy.InPlace;

        /// <summary>
        /// Helper method to cancel a workflow instance during migration if its state is unmigratable or deprecated.
        /// Halts migration execution by throwing a WorkflowMigrationCancelledException.
        /// </summary>
        /// <param name="reason">Reason for canceling the migration.</param>
        protected void CancelInstance(string reason)
        {
            throw new WorkflowMigrationCancelledException(reason);
        }

        /// <summary>
        /// Callback invoked during CancelAndRespawn strategy, allowing developers to start a new V2 instance
        /// with mapped inputs, state, and main workflow method object using the runner client / orchestrator.
        /// </summary>
        /// <param name="old">The V1 workflow instance wrapper.</param>
        /// <param name="runnerClient">The orchestrator/runner client for spawning instances.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The newly spawned V2 WorkflowInstanceId (Guid), or null if not spawned.</returns>
        public virtual Task<Guid?> OnMigrateAsync(TOld old, IOrchestrator runnerClient, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Guid?>(null);
        }

        /// <summary>
        /// Callback invoked during CancelAndRespawn strategy, allowing developers to start a new V2 instance
        /// with mapped inputs, state, and main workflow method object using the runner client / orchestrator.
        /// </summary>
        /// <param name="old">The V1 workflow instance wrapper.</param>
        /// <param name="new">The V2 workflow instance wrapper.</param>
        /// <param name="runnerClient">The orchestrator/runner client for spawning instances.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The newly spawned V2 WorkflowInstanceId (Guid), or null if not spawned.</returns>
        public virtual Task<Guid?> OnMigrateAsync(TOld old, TNew _new, IOrchestrator runnerClient, CancellationToken cancellationToken = default)
        {
            return OnMigrateAsync(old, runnerClient, cancellationToken);
        }

        /// <summary>Phase 1: Migrate strongly typed POCO state fields from V1 to V2.</summary>
        public abstract void MigrateState(TOld old, TNew _new);

        /// <summary>Alias for MigrateState for backward compatibility.</summary>
        public virtual void MigrateInstance(TOld old, TNew _new) => MigrateState(old, _new);

        /// <summary>
        /// Phase 2: Map each active wait DTO to its V2 equivalent and target StateIndex. Called once per
        /// active wait (including recursion into GroupWait children — the engine handles recursion).
        /// </summary>
        public abstract MigratedWait MigrateActiveWait(WaitInfrastructureDto oldWait, TNew _new);

        /// <summary>
        /// Phase 3: Called after MigrateActiveWait returns a SubWorkflowWait, to remap
        /// the child's frozen StateIndex and local variables to V2.
        /// Default: auto-remap the child's wait by name in V2's manifest.
        /// Override only when the sub-workflow's internal step sequence changed.
        /// </summary>
        public virtual Wait MigrateSubWorkflowState(SubWorkflowWaitDto oldSubWait, TNew _new)
            => SubWorkflow_RecreateWait(oldSubWait.MethodFullPath, oldSubWait.WaitName);
    }
}
