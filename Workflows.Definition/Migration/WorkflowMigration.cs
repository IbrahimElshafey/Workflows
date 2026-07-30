using Workflows.Abstraction.DTOs.Waits;

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
