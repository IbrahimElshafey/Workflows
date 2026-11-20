namespace Workflows.Abstraction.Enums
{
    public enum WorkflowInstanceStatus
    {
        New = 0,
        InProgress = 100,
        Completed = 200,
        InError = 300,
        Canceled = 400,
        Running = 500,

        /// <summary>
        /// Compensation (saga rollback) could not be completed. The instance is stuck mid-rollback
        /// and requires operator intervention (Admin UI "Stuck Rollbacks") or a retry.
        /// </summary>
        CompensationFailed = 600,

        /// <summary>
        /// A V1 -> V2 state migration threw. The instance is kept on its original version with its
        /// pre-migration state, and the snapshot written before the attempt is the recovery point.
        /// </summary>
        MigrationFailed = 700,
    }
}