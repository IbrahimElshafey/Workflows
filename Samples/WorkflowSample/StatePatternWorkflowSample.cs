using WorkflowSample.DataObject;
using Workflows.Definition;

namespace WorkflowSample
{
    public sealed class StatePatternWorkflowSample : WorkflowContainer
    {
        public int CurrentOrderId { get; set; }
        public string CurrentCustomer { get; set; }

        private sealed class SignalState
        {
            public int MinOrderId { get; set; }
            public string Status { get; set; }
        }

        private sealed class CommandState
        {
            public int OrderId { get; set; }
            public string Status { get; set; }
        }

        private sealed class CancelState
        {
            public int OrderId { get; set; }
            public string Reason { get; set; }
        }

        public override async IAsyncEnumerable<Wait> ExecuteWorkflowAsync()
        {
            yield return WaitSignal<OrderReceivedEvent>("OrderReceived", "Receive order with explicit state")
                .WithState(new SignalState { MinOrderId = 1, Status = "Received" })
                .MatchIf((signal, state) => signal.OrderId >= state.MinOrderId)
                .AfterMatch((OrderReceivedEvent signal, SignalState state) =>
                {
                    CurrentOrderId = signal.OrderId;
                    CurrentCustomer = signal.CustomerEmail;
                    Console.WriteLine($"[StatePattern] AfterMatch status: {state.Status} for order {signal.OrderId}");
                });

            yield return WaitDelay(TimeSpan.FromSeconds(1), "Delay with state cancel")
                .WithState(new CancelState { OrderId = CurrentOrderId, Reason = "Delay timeout" })
                .OnCanceled((CancelState state) =>
                {
                    Console.WriteLine($"[StatePattern] Delay canceled for order {state.OrderId}: {state.Reason}");
                    return ValueTask.CompletedTask;
                });

            yield return ExecuteCommand<ProcessPaymentCommand, ProcessPaymentResult>(
                    "ProcessPayment",
                    new ProcessPaymentCommand
                    {
                        OrderId = CurrentOrderId.ToString(),
                        Amount = 150,
                        PaymentMethod = "Card"
                    })
                .WithState(new CommandState { OrderId = CurrentOrderId, Status = "PaymentPending" })
                .WithRetries(maxAttempts: 2, backoff: TimeSpan.FromSeconds(1))
                .OnResult((ProcessPaymentResult result, CommandState state) =>
                {
                    Console.WriteLine($"[StatePattern] Payment result for order {state.OrderId} status:{state.Status} tx:{result.TransactionId}");
                })
                .OnFailure((Exception ex, CommandState state) =>
                {
                    Console.WriteLine($"[StatePattern] Payment failed for order {state.OrderId}: {ex.Message}");
                    return ValueTask.CompletedTask;
                })
                .RegisterCompensation((ProcessPaymentResult result, CommandState state) =>
                {
                    Console.WriteLine($"[StatePattern] Compensation for order {state.OrderId}, tx:{result.TransactionId}");
                    return ValueTask.CompletedTask;
                });

            yield return WaitGroup(
                    [
                        (SignalWait<ShippingEvent>)WaitSignal<ShippingEvent>("InventoryAllocated").WithState(CurrentOrderId).MatchIf((x, orderId) => x.OrderId == orderId),
                        (SignalWait<ShippingEvent>)WaitSignal<ShippingEvent>("LabelPrinted").WithState(CurrentOrderId).MatchIf((x, orderId) => x.OrderId == orderId)
                    ],
                    "Stateful group wait")
                .MatchIf(() => CurrentOrderId > 0);

            yield return WaitSubWorkflow(ShippingSubWorkflow(), "Stateful sub-workflow")
                .WithState(new CancelState { OrderId = CurrentOrderId, Reason = "Sub-workflow canceled" })
                .OnCanceled((CancelState state) =>
                {
                    Console.WriteLine($"[StatePattern] Sub-workflow canceled for order {state.OrderId}: {state.Reason}");
                    return ValueTask.CompletedTask;
                });

            yield return Compensate("ORDER_CANCEL")
                .WithState(new CancelState { OrderId = CurrentOrderId, Reason = "Manual compensation" })
                .OnCanceled((CancelState state) =>
                {
                    Console.WriteLine($"[StatePattern] Compensation canceled for order {state.OrderId}: {state.Reason}");
                    return ValueTask.CompletedTask;
                });
        }

        private async IAsyncEnumerable<Wait> ShippingSubWorkflow()
        {
            yield return WaitSignal<ShippingEvent>("OrderShipped", "Stateful shipping signal")
                .WithState(CurrentOrderId)
                .AfterMatch((ShippingEvent shipping, int orderId) =>
                {
                    Console.WriteLine($"[StatePattern] Shipping matched for order {orderId}, tracking:{shipping.TrackingNumber}");
                })
                .MatchIf((shipping, orderId) => shipping.OrderId == orderId);

            await Task.CompletedTask;
        }
    }
}
