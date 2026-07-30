using System;
using System.Collections.Generic;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Migration;
using Workflows.Definition;

namespace MyCompany.Workflows.Fulfillment.Migrations
{
    // =============================================================================
    // CONTEXT: What changed from V1 to V2?
    // =============================================================================
    //
    // FulfillmentWorkflow orchestrates order fulfillment through sub-workflows.
    //
    // V1 structure (Run method):
    //   yield return WaitSubWorkflow(InventorySubWorkflow(),  "ReserveInventory")
    //   yield return WaitSubWorkflow(ShippingSubWorkflow(),   "ArrangeShipping")
    //   yield return WaitGroup(
    //       [
    //           WaitSubWorkflow(BillingSubWorkflow(),  "RunBilling"),
    //           WaitSubWorkflow(NotifySubWorkflow(),   "NotifyCustomer"),
    //       ],
    //       "PostShipmentTasks"
    //   ).MatchAll()
    //
    // V2 changes:
    //   1. InventorySubWorkflow  → renamed to StockReservationSubWorkflow (steps identical)
    //   2. ShippingSubWorkflow   → "BookCarrier" step replaced by "SelectCarrier"+"ConfirmPickup"
    //   3. BillingSubWorkflow    → unchanged
    //   4. NotifySubWorkflow     → removed; notifications now internal to ShippingSubWorkflow
    //                             PostShipmentTasks group collapses to single BillingSubWorkflow
    //   5. FulfillmentWorkflow   → new field CarrierCode, removed field LegacyShipRef
    // =============================================================================

    /// <summary>
    /// Migration class for FulfillmentWorkflow v1.0.0 → v2.0.0.
    ///
    /// Inherits WorkflowMigration<TOld, TNew> which itself inherits MigrationContainer.
    /// This gives direct access to WaitSignal<T>, WaitSubWorkflow, WaitGroup, WaitDelay, etc.
    /// — the same DSL used when writing workflow code, no wrapper or adapter needed.
    ///
    /// Auto-generated skeleton by the Source Generator; conflict stubs completed by developer.
    /// </summary>
    public class FulfillmentWorkflowMigration_20260529
        : WorkflowMigration<FulfillmentWorkflowV1, FulfillmentWorkflowV2>
    {
        // =========================================================================
        // PHASE 1 — Instance migration
        // Map the parent workflow's class-level fields (the <>4__this object).
        // Called once per migrated instance.
        // =========================================================================

        [WorkflowMigration("FulfillmentWorkflow", "1.0.0", "2.0.0")]
        public override void MigrateInstance(FulfillmentWorkflowV1 old, FulfillmentWorkflowV2 _new)
        {
            // Auto-map all fields with the same name + type (OrderId, CustomerId, etc.)
            _new.AutoMapFrom(old);

            // [CONFLICT] 'LegacyShipRef' deleted in V2 — archive it for the audit trail.
            AddToOld(old.Instance).Property("LegacyShipRef_Archived", old.Instance.LegacyShipRef);

            // [CONFLICT] 'CarrierCode' is new in V2 — no V1 source exists.
            // Default to "PENDING"; ShippingSubWorkflow will populate it when it
            // resumes at the new "SelectCarrier" step.
            _new.Instance.CarrierCode = "PENDING";
        }

        // =========================================================================
        // PHASE 2 — Active wait migration
        // Called once per active wait DTO at the parent level.
        // The engine also calls this recursively for each child inside a GroupWaitDto.
        //
        // WaitSignal<T>, WaitSubWorkflow, WaitGroup come from MigrationContainer —
        // identical syntax to the workflow DSL, no wrapper or adapter needed.
        // =========================================================================

        public override MigratedWait MigrateActiveWait(WaitInfrastructureDto oldWait, FulfillmentWorkflowV2 _new)
        {
            switch (oldWait)
            {
                // -----------------------------------------------------------------
                // CASE 1: Parent was suspended waiting for an order confirmation
                // signal that is structurally unchanged in V2, but we want to
                // reinstate its MatchIf and AfterMatch handlers using the real DSL.
                // _new.Instance gives access to the migrated V2 domain fields.
                // -----------------------------------------------------------------
                case SignalWaitDto sig when sig.WaitName == "WaitOrderConfirmed":
                    return WaitSignal<OrderConfirmedEvent>("OrderConfirmed", "WaitOrderConfirmed")
                        .MatchIf(s => s.OrderId == _new.Instance.OrderId)
                        .AfterMatch(s =>
                        {
                            // Populate V2 instance fields from the incoming signal
                            _new.Instance.ConfirmedAt = s.ConfirmedAt;
                            _new.Instance.CarrierCode = s.PreferredCarrier;
                        });

                // -----------------------------------------------------------------
                // CASE 2: Suspended inside InventorySubWorkflow (now renamed).
                // Steps are identical — engine auto-remaps the child's StateIndex
                // by wait name (MigrateSubWorkflowState default handles it).
                // Runner omitted: engine resolves StockReservationSubWorkflow
                // from the V2 manifest by name.
                // -----------------------------------------------------------------
                case SubWorkflowWaitDto sub when sub.MethodFullPath.EndsWith("InventorySubWorkflow"):
                    return WaitSubWorkflow("StockReservationSubWorkflow");

                // -----------------------------------------------------------------
                // CASE 3: Suspended inside ShippingSubWorkflow.
                // Internal structure changed — engine will call MigrateSubWorkflowState
                // below to remap the child's frozen checkpoint by name.
                // -----------------------------------------------------------------
                case SubWorkflowWaitDto sub when sub.MethodFullPath.EndsWith("ShippingSubWorkflow"):
                    return WaitSubWorkflow("ShippingSubWorkflow");

                // -----------------------------------------------------------------
                // CASE 4: Suspended inside PostShipmentTasks group.
                // Group shape changed — NotifySubWorkflow is gone in V2.
                // Intercept the GroupWaitDto and decide based on child completion.
                // WaitGroup / WaitSubWorkflow below are from MigrationContainer —
                // same DSL as in workflow code.
                // -----------------------------------------------------------------
                case GroupWaitDto group when group.WaitName == "PostShipmentTasks":
                {
                    var billing = group.Child("RunBilling");

                    if (billing?.Status == WaitStatus.Completed)
                    {
                        // Billing already done. NotifySubWorkflow is gone.
                        // Dispatch audit event and recreate the terminal signal wait.
                        ScheduleCommand(new FulfillmentCompletedAuditCommand(_new.Instance.OrderId));

                        return WaitSignal<FulfillmentAcknowledgedEvent>(
                            "FulfillmentAcknowledged",
                            "FulfillmentCompleted"
                        );
                    }

                    // Billing still in-flight — collapse group to single sub-workflow.
                    // NotifySubWorkflow's frozen state is orphaned and cleaned up by engine.
                    // Engine calls MigrateSubWorkflowState for BillingSubWorkflow next;
                    // its steps are unchanged so the default auto-remap handles it.
                    return WaitSubWorkflow("BillingSubWorkflow");
                }

                // -----------------------------------------------------------------
                // DEFAULT: All other parent-level waits are identical in V1 and V2.
                // RecreateWait resolves the V2 StateIndex from the manifest by name.
                // -----------------------------------------------------------------
                default:
                    return RecreateWait(oldWait.WaitName);
            }
        }

        // =========================================================================
        // PHASE 3 — Sub-workflow internal state migration (optional override)
        // Called by the engine after MigrateActiveWait returns a SubWorkflowWait.
        // Only needed when the internal steps of the sub-workflow changed.
        // =========================================================================

        public override Wait MigrateSubWorkflowState(SubWorkflowWaitDto oldSubWait, FulfillmentWorkflowV2 _new)
        {
            // -----------------------------------------------------------------
            // ShippingSubWorkflow internal steps changed:
            //   V1: ... → "BookCarrier" → "ConfirmDeliveryDate"
            //   V2: ... → "SelectCarrier" → "ConfirmPickup" → "ConfirmDeliveryDate"
            //
            // If frozen at "BookCarrier" (removed in V2), redirect to "SelectCarrier".
            // CarrierCode = "PENDING" was already written in MigrateInstance.
            // -----------------------------------------------------------------
            if (oldSubWait.MethodFullPath.EndsWith("ShippingSubWorkflow"))
            {
                return oldSubWait.WaitName switch
                {
                    "BookCarrier" =>
                        // No direct equivalent — redirect to the V2 entry point of this stage.
                        // Engine resolves "SelectCarrier" StateIndex from the V2 ShippingSubWorkflow CFG.
                        SubWorkflow_RecreateWait("ShippingSubWorkflow", "SelectCarrier"),

                    _ =>
                        // All other ShippingSubWorkflow wait names exist in V2, just with a
                        // shifted StateIndex — engine remaps automatically by name.
                        SubWorkflow_RecreateWait("ShippingSubWorkflow", oldSubWait.WaitName),
                };
            }

            // StockReservationSubWorkflow, BillingSubWorkflow: steps unchanged.
            // Base default auto-remaps by wait name.
            return base.MigrateSubWorkflowState(oldSubWait, _new);
        }
    }

    // =============================================================================
    // Reference: typed state wrappers (auto-generated from the workflow schema)
    // These only carry the instance data — no DSL factory methods.
    // =============================================================================

    #region Auto-generated state wrappers

    public class FulfillmentWorkflowV1 : WorkflowStateWrapper<FulfillmentWorkflowV1Instance> { }
    public class FulfillmentWorkflowV2 : WorkflowStateWrapper<FulfillmentWorkflowV2Instance> { }

    public class FulfillmentWorkflowV1Instance
    {
        public Guid OrderId { get; set; }
        public Guid CustomerId { get; set; }
        public string LegacyShipRef { get; set; }       // removed in V2
    }

    public class FulfillmentWorkflowV2Instance
    {
        public Guid OrderId { get; set; }
        public Guid CustomerId { get; set; }
        public string CarrierCode { get; set; }         // new in V2
        public DateTime ConfirmedAt { get; set; }       // new in V2
    }

    public class FulfillmentCompletedAuditCommand
    {
        public Guid OrderId { get; }
        public FulfillmentCompletedAuditCommand(Guid orderId) => OrderId = orderId;
    }

    // Domain events
    public record OrderConfirmedEvent(Guid OrderId, DateTime ConfirmedAt, string PreferredCarrier);
    public record FulfillmentAcknowledgedEvent(Guid OrderId);

    #endregion
}
