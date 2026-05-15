using System.Collections.Generic;
using Workflows.Definition;
using Workflows.Runner.Tests.TestData;

namespace Workflows.Runner.Tests.TestWorkflows
{
    public class CancellationTestWorkflow : WorkflowContainer
    {
        public List<string> ExecutionLog { get; } = new();

        public override async IAsyncEnumerable<Wait> ExecuteWorkflowAsync()
        {
            ExecutionLog.Add("Start");

            // Signal with cancel token
            yield return WaitSignal<OrderReceivedSignal>("OrderReceived", "Wait for order")
                .WithState(100)
                .WithCancelToken("OrderFlow")
                .MatchIf((signal, minAmount) => signal.Amount > minAmount)
                .AfterMatch((signal, minAmount) =>
                {
                    ExecutionLog.Add($"Order received: {signal.OrderId}");
                })
                .OnCanceled((minAmount) =>
                {
                    ExecutionLog.Add($"Order wait cancelled with state: {minAmount}");
                    return ValueTask.CompletedTask;
                });

            // Another signal with same token
            yield return WaitSignal<ShipmentSignal>("ShipmentReady", "Wait for shipment")
                .WithCancelToken("OrderFlow")
                .AfterMatch((signal) =>
                {
                    ExecutionLog.Add($"Shipment ready: {signal.TrackingNumber}");
                });

            // Conditional cancellation
            if (ShouldCancel)
            {
                ExecutionLog.Add("Cancelling order flow");
                Cancel("OrderFlow");
            }

            // This should be skipped if cancelled
            yield return WaitSignal<PaymentConfirmedSignal>("PaymentConfirmed", "Wait for payment")
                .WithCancelToken("OrderFlow")
                .AfterMatch((signal) =>
                {
                    ExecutionLog.Add("Payment confirmed");
                });

            ExecutionLog.Add("End");
        }

        public bool ShouldCancel { get; set; }
    }
}
