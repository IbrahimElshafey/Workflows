using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkflowSample.DataObject;
using Workflows.Definition;

namespace WorkflowSample
{
    // --- The Workflow Definition ---
    public sealed class OrderProcessingWorkflow : WorkflowContainer
    {
        // 1. DOMAIN STATE: These are stable '<>4__this' properties.
        // They will be safely captured as the MachineState.Instance.
        public int CurrentOrderId { get; set; }
        public string CurrentCustomer { get; set; }
        public int ProcessCount { get; set; }

        public override async IAsyncEnumerable<Wait> ExecuteWorkflowAsync()
        {
            // Initialize domain state
            ProcessCount = 10;
            int minOrderId = 0; // Local variable, but we will NOT capture it in a closure

            // 1. Wait for a specific signal (Order Submitted)
            // We use .WithState(minOrderId) to explicitly pass the local variable as a data contract
            yield return WaitSignal<OrderSubmittedEvent>("OrderSubmittedSignal", "Wait for Order Submission")
                .WithState(minOrderId)
                .MatchIf((order, minId) => order.OrderId > minId) // Pure lambda (no closure)
                .AfterMatch((order) =>
                {
                    // Accessing 'this.' creates a stable <>4__this pointer, NOT a volatile closure
                    CurrentOrderId = order.OrderId;
                    CurrentCustomer = order.CustomerName;
                    ProcessCount -= 5;

                    Console.WriteLine($"[Workflow] Order {CurrentOrderId} received from {CurrentCustomer}.");
                });

            // 2. Wait for a specific amount of time
            yield return WaitDelay(TimeSpan.FromMinutes(5), "Wait 5 minutes before charging");

            // 3. Wait for payment
            // Explicitly pass this.CurrentOrderId as the state context to avoid capturing 'this' implicitly in MatchIf
            yield return WaitSignal<PaymentProcessedEvent>("PaymentProcessedSignal", "Wait for Payment")
                .WithState(CurrentOrderId)
                .MatchIf((payment, targetOrderId) => payment.OrderId == targetOrderId)
                .AfterMatch((payment, targetOrderId) =>
                {
                    Console.WriteLine($"[Workflow] Payment success for {targetOrderId}: {payment.IsSuccessful}");
                });

            // 4. Parallel Fulfillment Prep
            // Explicitly passing CurrentOrderId into the child waits
            yield return WaitGroup(
               [
                    WaitSignal<ShippingEvent>("InventoryAllocated")
                        .WithState(CurrentOrderId)
                        .MatchIf((shipping, targetId) => shipping.OrderId == targetId).AsWait(),

                    WaitSignal<ShippingEvent>("LabelPrinted")
                        .WithState(CurrentOrderId)
                        .MatchIf((shipping, targetId) => shipping.OrderId == targetId).AsWait()
                ],
                "Parallel Fulfillment Prep"
            ).MatchIf(() => ProcessCount >= 10); // Domain state evaluation

            // 5. Yield execution to a sub-workflow
            yield return WaitSubWorkflow(ShippingSubWorkflow(), "Run Shipping Sub-Workflow");

            Console.WriteLine($"[Workflow] Order {CurrentOrderId} processing complete!");
        }

        /// <summary>
        /// Sub-workflows also strictly follow the Explicit State Hand-off rule.
        /// </summary>
        public async IAsyncEnumerable<Wait> ShippingSubWorkflow()
        {
            var marker = "test";

            // Pass a Tuple to .WithState to pass multiple variables cleanly
            yield return WaitSignal<ShippingEvent>("OrderShipped", "Wait for Courier Pickup")
                .WithState((OrderId: CurrentOrderId, Marker: marker))
                .MatchIf((shipping, state) => shipping.OrderId == state.OrderId && state.Marker == "test")
                .AfterMatch((shipping, state) =>
                {
                    Console.WriteLine($"[SubWorkflow] Order shipped! Tracking: {shipping.TrackingNumber}");
                })
                .WithCancelToken("sdldfjk")
                .OnCanceled(async (state) =>
                {
                    Console.WriteLine($"[SubWorkflow] Order shipment canceled for Order {state.OrderId} marker:{state.Marker}");
                    await Task.CompletedTask;
                });

            await Task.Delay(100);
            Console.WriteLine("After waiting for shipping event, doing some async work in the sub-workflow...");
        }
    }
}