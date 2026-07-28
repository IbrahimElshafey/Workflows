using System;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;
using Workflows.Runner.Migration;

namespace Workflows.Migrations
{
    /// <summary>
    /// Auto-generated migration script for OrderProcessingWorkflow from V1 to V2.
    /// </summary>
    [WorkflowMigration("OrderProcessingWorkflow", fromVersion: 1, toVersion: 2)]
    public class OrderProcessingWorkflowMigration_V1_To_V2 : WorkflowMigration
    {
        // =========================================================================
        // 1. STATE MIGRATION (Phase 1)
        // =========================================================================
        /// <summary>
        /// Migrates top-level workflow state properties from V1 to V2.
        /// </summary>
        public override void MigrateState(dynamic old, dynamic _new)
        {
            // Fail-Loud AutoMapFrom Scaffolding:
            // Copies matching properties automatically between V1 and V2 state.
            _new.AutoMapFrom(old);

            // Example custom property transformations:
            // _new.State.AmountInCents = (int)(old.State.Amount * 100);
            // _new.State.ShippingAddress = old.State.Address;
        }

        // =========================================================================
        // 2. ACTIVE WAIT STATE MIGRATION & RECREATION (Phase 2)
        // =========================================================================
        /// <summary>
        /// Recreates active suspended waits for in-flight instances.
        /// Switch based on oldWait.WaitName to customize payload or wait types.
        /// </summary>
        public override Wait MigrateActiveWait(WaitInfrastructureDto oldWait, dynamic _new)
        {
            switch (oldWait.WaitName)
            {
                case "ApprovalWait":
                    // Custom wait payload or signal identifier remapping:
                    // return WaitSignal<UpdatedApprovalWaitPayload>("ApprovalWaitSignal", oldWait.WaitName).Build();
                    return RecreateWait(oldWait.WaitName);

                case "PaymentCallbackWait":
                    // Custom wait payload or signal identifier remapping:
                    // return WaitSignal<UpdatedPaymentCallbackWaitPayload>("PaymentCallbackWaitSignal", oldWait.WaitName).Build();
                    return RecreateWait(oldWait.WaitName);

                case "CustomerNotificationWait":
                    // Custom wait payload or signal identifier remapping:
                    // return WaitSignal<UpdatedCustomerNotificationWaitPayload>("CustomerNotificationWaitSignal", oldWait.WaitName).Build();
                    return RecreateWait(oldWait.WaitName);

                default:
                    // Default auto-recreation of wait by name using V2's compiled CFG manifest.
                    return RecreateWait(oldWait.WaitName);
            }
        }

        // =========================================================================
        // 3. SUB-WORKFLOW STATE MIGRATION (Phase 3)
        // =========================================================================
        /// <summary>
        /// Remaps child sub-workflow state pointers and frozen state indices.
        /// </summary>
        public override Wait MigrateSubWorkflowState(SubWorkflowWaitDto oldSubWait, dynamic _new)
        {
            switch (oldSubWait.MethodFullPath)
            {
                case "PaymentProcessorSubWorkflow":
                    // Recreates sub-workflow wait and remaps child CFG pointers:
                    return SubWorkflow_RecreateWait(oldSubWait.MethodFullPath, oldSubWait.WaitName);

                case "InventoryAllocationSubWorkflow":
                    // Recreates sub-workflow wait and remaps child CFG pointers:
                    return SubWorkflow_RecreateWait(oldSubWait.MethodFullPath, oldSubWait.WaitName);

                default:
                    return SubWorkflow_RecreateWait(oldSubWait.MethodFullPath, oldSubWait.WaitName);
            }
        }
    }

    // =========================================================================
    // 4. STRONGLY-TYPED POCO CONTRACT SNAPSHOTS
    // =========================================================================
    public class OrderProcessingWorkflowState_V1
    {
        public Guid OrderId { get; set; }
        public decimal Amount { get; set; }
        public string CustomerId { get; set; } = "";
    }

    public class OrderProcessingWorkflowState_V2
    {
        public Guid OrderId { get; set; }
        public double Amount { get; set; }
        public string CustomerId { get; set; } = "";
        public int AmountInCents { get; set; }
    }

    public class PaymentProcessorSubWorkflowState_V1
    {
        public string TransactionId { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class PaymentProcessorSubWorkflowState_V2
    {
        public string TransactionId { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime ProcessedUtc { get; set; }
    }

    public class InventoryAllocationSubWorkflowState_V1
    {
        public string TransactionId { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class InventoryAllocationSubWorkflowState_V2
    {
        public string TransactionId { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime ProcessedUtc { get; set; }
    }
}
