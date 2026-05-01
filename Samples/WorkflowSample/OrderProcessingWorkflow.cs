namespace Workflows.Sample
{
    // --- The Workflow Definition ---
    public class OrderProcessingWorkflow : Definition.WorkflowContainer
    {
        // Workflow State: This will be serialized and persisted automatically
        public int CurrentOrderId { get; set; }

        public string CurrentCustomer { get; set; }

        /// <summary>
        /// The main entry point for the workflow.
        /// </summary>
        public async IAsyncEnumerable<Definition.Wait> ProcessOrderWorkflow()
        {
            // 1. Wait for a specific signal (Order Submitted)
            yield return WaitSignal<OrderSubmittedEvent>("OrderSubmittedSignal", "Wait for Order Submission")
                // MatchIf acts as a filter expression to only accept valid payloads
                .MatchIf(order => order.OrderId > 0)
                // AfterMatch captures data into the workflow's state
                .AfterMatch(
                    order =>
                    {
                        CurrentOrderId = order.OrderId;
                        CurrentCustomer = order.CustomerName;
                        Console.WriteLine($"[Workflow] Order {CurrentOrderId} received from {CurrentCustomer}.");
                    });

            // 2. Wait for a specific amount of time (e.g., cooling off period for cancellation)
            yield return WaitDelay(TimeSpan.FromMinutes(5), "Wait 5 minutes before charging");

            // 3. Wait for payment, correlating it to the captured CurrentOrderId
            yield return WaitSignal<PaymentProcessedEvent>("PaymentProcessedSignal", "Wait for Payment")
                .MatchIf(payment => payment.OrderId == CurrentOrderId)
                .AfterMatch(
                    payment =>
                    {
                        Console.WriteLine($"[Workflow] Payment success: {payment.IsSuccessful}");
                    });

            // Yield a GroupWait that waits for ALL child waits to complete before continuing
            yield return WaitGroup(
                [ 
                    WaitSignal<ShippingEvent>("InventoryAllocated").MatchIf(x => x.OrderId == CurrentOrderId),
                    WaitSignal<ShippingEvent>("LabelPrinted").MatchIf(x => x.OrderId == CurrentOrderId) 
                ],
                "Parallel Fulfillment Prep")
                .MatchAll();

            // 5. Yield execution to a sub-workflow
            yield return WaitSubWorkflow(ShippingSubWorkflow(), "Run Shipping Sub-Workflow");

            Console.WriteLine($"[Workflow] Order {CurrentOrderId} processing complete!");
        }

        /// <summary>
        /// A sub-workflow that can be reused and called from the main workflow.
        /// </summary>
        public async IAsyncEnumerable<Definition.Wait> ShippingSubWorkflow()
        {
            yield return WaitSignal<ShippingEvent>("OrderShipped", "Wait for Courier Pickup")
                .MatchIf(shipping => shipping.OrderId == CurrentOrderId)
                .AfterMatch(
                    shipping =>
                    {
                        Console.WriteLine($"[SubWorkflow] Order shipped! Tracking: {shipping.TrackingNumber}");
                    });
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