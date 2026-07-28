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

        /// <summary>Alias for MigrateInstance to support MigrateState terminology.</summary>
        public virtual void MigrateState(TOld old, TNew _new) => MigrateInstance(old, _new);

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

    /// <summary>
    /// Dynamic base class for version migration classes without requiring pre-compiled state wrappers.
    /// </summary>
    public abstract class WorkflowMigration : MigrationContainer
    {
        /// <summary>Phase 1: Migrate class-level state fields. Called once per instance.</summary>
        public abstract void MigrateState(dynamic old, dynamic _new);

        /// <summary>Alias for MigrateState for backward compatibility.</summary>
        public virtual void MigrateInstance(dynamic old, dynamic _new) => MigrateState(old, _new);

        /// <summary>Phase 2: Map each active wait DTO to its V2 equivalent.</summary>
        public abstract Wait MigrateActiveWait(WaitInfrastructureDto oldWait, dynamic _new);

        /// <summary>Phase 3: Sub-workflow state remapping.</summary>
        public virtual Wait MigrateSubWorkflowState(SubWorkflowWaitDto oldSubWait, dynamic _new)
            => SubWorkflow_RecreateWait(oldSubWait.MethodFullPath, oldSubWait.WaitName);
    }
}
