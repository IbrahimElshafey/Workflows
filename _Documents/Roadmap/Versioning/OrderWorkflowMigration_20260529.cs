using System;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Migration;
using Workflows.Definition;

namespace MyCompany.Workflows.Billing.Migrations
{
    // =============================================================================
    // CONTEXT: What changed from V1 to V2?
    // =============================================================================
    //
    // OrderWorkflow manages the customer order lifecycle.
    //
    // V1 structure (Run method):
    //   yield return WaitSignal<OrderSubmittedEvent>("OrderSubmittedSignal", "WaitForOrder")
    //   yield return WaitDelay(TimeSpan.FromMinutes(5), "Wait 5 minutes before charging")
    //   yield return WaitSignal<PaymentProcessedEvent>("PaymentProcessedSignal", "WaitForPayment")
    //   yield return WaitGroup(
    //       [WaitSignal<ShippingEvent>("InventoryAllocated"), WaitSignal<ShippingEvent>("LabelPrinted")],
    //       "ParallelFulfillmentPrep"
    //   ).MatchIf(() => ProcessCount >= 10)
    //   yield return WaitSignal<ShippingEvent>("VerifyStock", "VerifyStock")
    //
    // V2 changes:
    //   1. OrderId (Guid)     → OrderNumber (string, prefixed "ORD-")
    //   2. CustomerName       → deleted; V2 uses CustomerEmail only
    //   3. Amount + Tax       → new fields (split from V1 CurrentTotal)
    //   4. "VerifyStock"      → replaced by "ParallelVerification" group
    //                          (VerifyStock + CustomerCheck run in parallel)
    //   5. "StockConfirmed"   → if already completed, must manually schedule
    //                          RequestPaymentCommand to unblock the next step
    // =============================================================================

    /// <summary>
    /// Migration class for OrderWorkflow v1.0.0 → v2.0.0.
    ///
    /// Inherits WorkflowMigration&lt;TOld, TNew&gt; which in turn inherits MigrationContainer.
    /// Wait factory methods (WaitSignal, WaitGroup, WaitDelay, WaitSubWorkflow,
    /// RecreateWait, ScheduleCommand) are called directly on 'this' — identical
    /// syntax to authoring workflow code. _new only exposes .Instance for domain data.
    ///
    /// Auto-generated skeleton by the Source Generator; conflict stubs completed by developer.
    /// </summary>
    public class OrderWorkflowMigration_20260529
        : WorkflowMigration<OrderWorkflowV1, OrderWorkflowV2>
    {
        // =========================================================================
        // PHASE 1 — Instance migration
        // Map the parent workflow's class-level fields (the <>4__this object).
        // Called once per migrated instance.
        // =========================================================================

        [WorkflowMigration("OrderWorkflow", "1.0.0", "2.0.0")]
        public override void MigrateInstance(OrderWorkflowV1 old, OrderWorkflowV2 _new)
        {
            // A. Auto-map matching fields (CustomerEmail, ShippingAddress, ProcessCount, ...)
            //    whose name and type did not change from V1 to V2.
            _new.AutoMapFrom(old);

            // B. Renamed / re-typed field.
            //    V1: OrderId (Guid) → V2: OrderNumber (string, prefixed "ORD-")
            _new.Instance.OrderNumber = $"ORD-{old.Instance.OrderId.ToString().Substring(0, 8).ToUpper()}";

            // C. New fields added in V2 — split from V1's single CurrentTotal field.
            _new.Instance.Amount = old.Instance.CurrentTotal;
            _new.Instance.Tax    = old.Instance.CurrentTotal * 0.15m;  // 15% VAT default

            // [CONFLICT] CustomerName deleted in V2.
            // Archived for audit trail on the V1 instance record.
            AddToOld(old.Instance).Property("CustomerName_Archived", old.Instance.CustomerName);

            // Audit metadata written into the old instance for migration traceability.
            AddToOld(old.Instance).Property("MigratedAt",      DateTime.UtcNow);
            AddToOld(old.Instance).Property("MigrationBatchId", Guid.NewGuid());
        }

        // =========================================================================
        // PHASE 2 — Active wait migration
        // Called once per active wait DTO (and recursively for GroupWaitDto children).
        //
        // WaitSignal<T>, WaitGroup, WaitDelay, RecreateWait, ScheduleCommand are
        // inherited from MigrationContainer — same DSL as workflow authoring code.
        // _new.Instance provides access to the already-migrated V2 domain fields.
        // =========================================================================

        public override MigratedWait MigrateActiveWait(WaitInfrastructureDto oldWait, OrderWorkflowV2 _new)
        {
            // -----------------------------------------------------------------
            // CASE A: Suspended at "VerifyStock" in V1.
            // In V2 this single signal wait was replaced by a two-signal parallel
            // group "ParallelVerification" (stock check + customer verification).
            // We rebuild the group here using the native DSL directly.
            // -----------------------------------------------------------------
            if (oldWait.WaitName == "VerifyStock")
            {
                return WaitGroup(
                    [
                        WaitSignal<StockVerifiedEvent>("StockVerified",    "VerifyStock"),
                        WaitSignal<CustomerCheckedEvent>("CustomerChecked", "VerifyCustomer"),
                    ],
                    "ParallelVerification"
                ).MatchAll();
            }

            // -----------------------------------------------------------------
            // CASE B: "StockConfirmed" was Completed before migration ran.
            // The instance already passed the stock gate, but the V1 state machine
            // never advanced to schedule the payment command (it was handled by
            // a yield point we are now bypassing).
            // Schedule the command transactionally, then recreate the payment wait.
            // -----------------------------------------------------------------
            if (oldWait.WaitName == "StockConfirmed" && oldWait.Status == WaitStatus.Completed)
            {
                ScheduleCommand(new RequestPaymentCommand(
                    _new.Instance.OrderNumber,
                    _new.Instance.Amount
                ));

                return WaitSignal<PaymentAuthorizedEvent>("PaymentAuthorized", "AuthorizePayment")
                    .MatchIf(p => p.OrderNumber == _new.Instance.OrderNumber)
                    .AfterMatch(p =>
                    {
                        _new.Instance.PaymentRef = p.TransactionId;
                    });
            }

            // -----------------------------------------------------------------
            // DEFAULT: All other waits are structurally identical in V1 and V2.
            // RecreateWait resolves the new StateIndex from the V2 CFG by name.
            // -----------------------------------------------------------------
            return RecreateWait(oldWait.WaitName);
        }
    }

    // =============================================================================
    // Reference: typed state wrappers (auto-generated from the workflow schema).
    // These only carry the instance data — no DSL factory methods.
    // =============================================================================

    #region Auto-generated state wrappers

    public class OrderWorkflowV1 : WorkflowStateWrapper<OrderWorkflowV1Instance> { }
    public class OrderWorkflowV2 : WorkflowStateWrapper<OrderWorkflowV2Instance> { }

    public class OrderWorkflowV1Instance
    {
        public Guid     OrderId       { get; set; }
        public string   CustomerName  { get; set; }     // deleted in V2
        public string   CustomerEmail { get; set; }
        public decimal  CurrentTotal  { get; set; }     // split into Amount + Tax in V2
        public int      ProcessCount  { get; set; }
    }

    public class OrderWorkflowV2Instance
    {
        public string   OrderNumber   { get; set; }     // renamed + re-typed from OrderId
        public string   CustomerEmail { get; set; }
        public decimal  Amount        { get; set; }     // new
        public decimal  Tax           { get; set; }     // new
        public int      ProcessCount  { get; set; }
        public string   PaymentRef    { get; set; }     // new — populated by AfterMatch
    }

    // Domain events
    public record StockVerifiedEvent(string OrderNumber);
    public record CustomerCheckedEvent(string OrderNumber);
    public record PaymentAuthorizedEvent(string OrderNumber, string TransactionId);

    // Commands
    public record RequestPaymentCommand(string OrderNumber, decimal Amount);

    #endregion
}
