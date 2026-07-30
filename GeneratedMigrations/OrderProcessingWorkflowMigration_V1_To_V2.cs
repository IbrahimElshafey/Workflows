using System;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;
using Workflows.Runner.Migration;

namespace Workflows.Migrations
{
    [WorkflowMigration("OrderProcessingWorkflow", fromVersion: 1, toVersion: 2)]
    public class OrderProcessingWorkflowMigration_V1_To_V2 : WorkflowMigration
    {
        public override void MigrateInstance(dynamic old, dynamic _new)
        {
            // Fail-Loud AutoMapFrom Scaffolding
            // Copies matching properties automatically; throws WorkflowMigrationException if mismatch occurs.
            _new.AutoMapFrom(old);
        }

        public override MigratedWait MigrateActiveWait(WaitInfrastructureDto oldWait, dynamic _new)
        {
            // Auto-generated wait recreation stub
            return RecreateWait(oldWait.WaitName);
        }
    }
}
