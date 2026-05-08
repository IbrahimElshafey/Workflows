using WorkflowSample.DataObject;
using Workflows.Definition;

namespace WorkflowSample
{
    public sealed class DefaultClosureWorkflowSample : WorkflowContainer
    {
        public int CurrentOrderId { get; set; }

        public override async IAsyncEnumerable<Wait> ExecuteWorkflowAsync()
        {
            var minOrderId = 1;
            var status = "Received by closure";

            yield return WaitSignal<OrderReceivedEvent>("OrderReceived", "Receive order with closure")
                .MatchIf(signal => signal.OrderId >= minOrderId)
                .AfterMatch(signal =>
                {
                    CurrentOrderId = signal.OrderId;
                    Console.WriteLine($"[DefaultClosure] AfterMatch status: {status} for order {signal.OrderId}");
                })
                .OnCanceled(async () =>
                {
                    Console.WriteLine($"[DefaultClosure] Signal canceled for order {CurrentOrderId}");
                    await Task.CompletedTask;
                });

            var delayReason = "Closure timeout";
            yield return WaitDelay(TimeSpan.FromSeconds(1), "Delay with closure cancel")
                .OnCanceled(async () =>
                {
                    Console.WriteLine($"[DefaultClosure] Delay canceled for order {CurrentOrderId}: {delayReason}");
                    await Task.CompletedTask;
                });

            var commandStatus = "PaymentPending";
            yield return ExecuteCommand<ProcessPaymentCommand, ProcessPaymentResult>(
                    "ProcessPayment",
                    new ProcessPaymentCommand
                    {
                        OrderId = CurrentOrderId.ToString(),
                        Amount = 150,
                        PaymentMethod = "Card"
                    })
                .WithRetries(maxAttempts: 2, backoff: TimeSpan.FromSeconds(1))
                .OnResult(result =>
                {
                    Console.WriteLine($"[DefaultClosure] Payment result for order {CurrentOrderId} status:{commandStatus} tx:{result.TransactionId}");
                })
                .OnFailure(async ex =>
                {
                    Console.WriteLine($"[DefaultClosure] Payment failed for order {CurrentOrderId}: {ex.Message}");
                    await Task.CompletedTask;
                })
                .RegisterCompensation(async result =>
                {
                    Console.WriteLine($"[DefaultClosure] Compensation for order {CurrentOrderId}, tx:{result.TransactionId}");
                    await Task.CompletedTask;
                });

            var groupThreshold = 0;
            yield return WaitGroup(
                [
                    (SignalWait<ShippingEvent>)WaitSignal<ShippingEvent>("InventoryAllocated").MatchIf(x => x.OrderId == CurrentOrderId),
                    (SignalWait<ShippingEvent>)WaitSignal<ShippingEvent>("LabelPrinted").MatchIf(x => x.OrderId == CurrentOrderId)
                ],
                "Closure group wait")
                .MatchIf(() => CurrentOrderId > groupThreshold);

            yield return WaitSubWorkflow(ShippingSubWorkflow(), "Closure sub-workflow")
                .OnCanceled(async () =>
                {
                    Console.WriteLine($"[DefaultClosure] Sub-workflow canceled for order {CurrentOrderId}");
                    await Task.CompletedTask;
                });

            yield return Compensate("ORDER_CANCEL")
                .OnCanceled(async () =>
                {
                    Console.WriteLine($"[DefaultClosure] Compensation canceled for order {CurrentOrderId}");
                    await Task.CompletedTask;
                });
        }

        private async IAsyncEnumerable<Wait> ShippingSubWorkflow()
        {
            yield return WaitSignal<ShippingEvent>("OrderShipped", "Closure shipping signal")
                .MatchIf(shipping => shipping.OrderId == CurrentOrderId)
                .AfterMatch(shipping =>
                {
                    Console.WriteLine($"[DefaultClosure] Shipping matched for order {CurrentOrderId}, tracking:{shipping.TrackingNumber}");
                });

            await Task.CompletedTask;
        }
    }
}
