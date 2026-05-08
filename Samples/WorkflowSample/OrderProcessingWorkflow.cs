using WorkflowSample.DataObject;
using Workflows.Definition;

namespace WorkflowSample
{
    // --- The Workflow Definition ---
    public sealed class OrderProcessingWorkflow : WorkflowContainer
    {
        private int _integerField;
        // Workflow State: This will be serialized and persisted automatically
        public int CurrentOrderId { get; set; }

        public string CurrentCustomer { get; set; }

        /// <summary>
        /// The main entry point for the workflow.
        /// </summary>
        public override async IAsyncEnumerable<Wait> ExecuteWorkflowAsync()
        {
            var count = 10;
            // 1. Wait for a specific signal (Order Submitted)
            yield return WaitSignal<OrderSubmittedEvent>("OrderSubmittedSignal", "Wait for Order Submission")
                .WithState(0)
                // MatchIf acts as a filter expression to only accept valid payloads
                .MatchIf((order, minOrderId) => order.OrderId > minOrderId)
                // AfterMatch captures data into the workflow's state
                .AfterMatch(
                    order =>
                    {
                        CurrentOrderId = order.OrderId;
                        CurrentCustomer = order.CustomerName;
                        //count -= 5;;
                        Console.WriteLine($"[Workflow] Order {CurrentOrderId} received from {CurrentCustomer}.");
                    });

            // 2. Wait for a specific amount of time (e.g., cooling off period for cancellation)
            yield return WaitDelay(TimeSpan.FromMinutes(5), "Wait 5 minutes before charging");

            // 3. Wait for payment, correlating it to the captured CurrentOrderId
            yield return PaymentWait(_integerField);

            // Yield a GroupWait that waits for ALL child waits to complete before continuing
            yield return WaitGroup(
                [ WaitSignal<ShippingEvent>("InventoryAllocated").WithState(CurrentOrderId).MatchIf((x, orderId) => x.OrderId == orderId), WaitSignal<ShippingEvent>(
                    "LabelPrinted")
                    .WithState(CurrentOrderId)
                    .MatchIf((x, orderId) => x.OrderId == orderId) ],
                "Parallel Fulfillment Prep")
                .MatchIf(() => count >= 10);

            // 5. Yield execution to a sub-workflow
            yield return WaitSubWorkflow(ShippingSubWorkflow(), "Run Shipping Sub-Workflow");

            Console.WriteLine($"[Workflow] Order {CurrentOrderId} processing complete!");
        }

        private SignalWait<PaymentProcessedEvent> PaymentWait(int count)
        {
            var x = 10;
            return WaitSignal<PaymentProcessedEvent>("PaymentProcessedSignal", "Wait for Payment")
                .WithState((CurrentOrderId, x))
                .MatchIf((payment, state) => payment.OrderId == state.CurrentOrderId && state.x > 0)
                .AfterMatch(
                    payment =>
                    {
                        Console.WriteLine($"[Workflow] Payment success: {payment.IsSuccessful} {count}");
                    });
        }

        /// <summary>
        /// A sub-workflow that can be reused and called from the main workflow.
        /// </summary>
        public async IAsyncEnumerable<Wait> ShippingSubWorkflow()
        {
            var yy = "test";
            yield return WaitSignal<ShippingEvent>("OrderShipped", "Wait for Courier Pickup")
                .WithState((CurrentOrderId, yy))
                .MatchIf((shipping, state) => shipping.OrderId == state.CurrentOrderId && state.yy == "test")
                .AfterMatch(
                    shipping =>
                    {
                        Console.WriteLine($"[SubWorkflow] Order shipped! Tracking: {shipping.TrackingNumber} - {_integerField}");
                    })
                .WithCancelToken("sdldfjk")
                .OnCanceled<(int CurrentOrderId, string yy)>(
                    async state =>
                    {
                        Console.WriteLine($"[SubWorkflow] Order shipment canceled for Order {state.CurrentOrderId} marker:{state.yy}");
                        await Task.CompletedTask;
                    });
            await Task.Delay(100);
            Console.WriteLine("After waiting for shipping event, doing some async work in the sub-workflow...");
            yy += "10";
        }

        // Optional Overrides from WorkflowContainer
        public override Task OnError(string message, Exception ex = null)
        {
            Console.WriteLine($"Error in Workflow: {message}");
            return base.OnError(message, ex);
        }

        public override Task OnCompleted()
        {
            Console.WriteLine("Workflow execution fully completed and state can be archived.");
            return base.OnCompleted();
        }
    }
}