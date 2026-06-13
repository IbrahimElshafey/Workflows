using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Definition
{
    /// <summary>
    /// Base class for all version migration classes. Inherit this, decorate with
    /// [WorkflowMigration], and register with services.AddWorkflowMigration&lt;&gt;().
    /// </summary>
    public abstract class WorkflowMigration<TOld, TNew> : MigrationContainer
        where TOld : WorkflowStateWrapper
        where TNew : WorkflowStateWrapper
    {
        /// <summary>Phase 1: Migrate class-level fields. Called once per instance.</summary>
        public abstract void MigrateInstance(TOld old, TNew _new);

        /// <summary>
        /// Phase 2: Map each active wait DTO to its V2 equivalent. Called once per
        /// active wait (including recursion into GroupWait children — the engine handles recursion).
        /// </summary>
        public abstract Wait MigrateActiveWait(WaitInfrastructureDto oldWait, TNew _new);

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
