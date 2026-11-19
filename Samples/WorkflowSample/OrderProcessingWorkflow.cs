using WorkflowSample.DataObject;
using Workflows.Definition;

namespace WorkflowSample
{
    // --- The Workflow Definition ---
    [Workflow("OrderProcessingWorkflow", 1)]
    public sealed partial class OrderProcessingWorkflow : WorkflowContainer
    {
        public int CurrentOrderId { get; set; }
        public string CurrentCustomer { get; set; } = string.Empty;
        public int ProcessCount { get; set; }

        public async IAsyncEnumerable<Wait> Run(OrderProcessingWorkflowState state = null!)
        {
            // Initialize domain state
            ProcessCount = 10;
            int minOrderId = 0; // Stack local - cannot be captured, so it is handed off via WithState

            // 1. Wait for a specific signal (Order Submitted)
            // Only the local minOrderId is handed off; CurrentCustomer is a property
            yield return WaitSignal<OrderSubmittedEvent>("OrderSubmittedSignal", "Wait for Order Submission")
                .WithState(minOrderId)
                .MatchIf((order, minId) => order.OrderId > minId && order.CustomerName == CurrentCustomer)
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
            // CurrentOrderId is a container property, so the matcher is rebound to the live instance
            yield return WaitSignal<PaymentProcessedEvent>("PaymentProcessedSignal", "Wait for Payment")
                .MatchIf((payment) => payment.OrderId == CurrentOrderId)
                .AfterMatch((payment) =>
                {
                    Console.WriteLine($"[Workflow] Payment success for {CurrentOrderId}: {payment.IsSuccessful}");
                });

            // 4. Parallel Fulfillment Prep
            // Child waits read CurrentOrderId from the live container - no WithState needed
            yield return WaitGroup(
               [
                    (SignalWait<ShippingEvent>)WaitSignal<ShippingEvent>("InventoryAllocated", "WaitInventoryAllocated")
                        .MatchIf((shipping) => shipping.OrderId == CurrentOrderId),

                    (SignalWait<ShippingEvent>)WaitSignal<ShippingEvent>("LabelPrinted", "WaitLabelPrinted")
                        .MatchIf((shipping) => shipping.OrderId == CurrentOrderId)
                ],
                "Parallel Fulfillment Prep"
            ).MatchIf(() => ProcessCount >= 10); // Domain state evaluation

            // 5. Yield execution to a sub-workflow
            yield return WaitSubWorkflow(ShippingSubWorkflow(new ShippingSubWorkflowState()), "Run Shipping Sub-Workflow");

            Console.WriteLine($"[Workflow] Order {CurrentOrderId} processing complete!");
        }

        /// <summary>
        /// Sub-workflows also strictly follow the Explicit State Hand-off rule.
        /// </summary>
        [SubWorkflow]
        private async IAsyncEnumerable<Wait> ShippingSubWorkflow(ShippingSubWorkflowState state = null!)
        {
            // Only stack locals need an explicit hand-off - CurrentOrderId is read from the live container
            var marker = "test";

            yield return WaitSignal<ShippingEvent>("OrderShipped", "Wait for Courier Pickup")
                .WithState(marker)
                .MatchIf((shipping, marker) => shipping.OrderId == CurrentOrderId && marker == "test")
                .AfterMatch((shipping) =>
                {
                    Console.WriteLine($"[SubWorkflow] Order shipped! Tracking: {shipping.TrackingNumber}");
                })
                .WithCancelToken("sdldfjk")
                .OnCanceled(async () =>
                {
                    Console.WriteLine($"[SubWorkflow] Order shipment canceled for Order {CurrentOrderId}");
                    await Task.CompletedTask;
                });

            await Task.Delay(100);
            Console.WriteLine("After waiting for shipping event, doing some async work in the sub-workflow...");
        }
    }

    public class OrderProcessingWorkflowState
    {
    }

    public class ShippingSubWorkflowState
    {
    }
}