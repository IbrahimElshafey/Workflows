using System.Collections.Generic;
using Workflows.Definition;
using Workflows.Runner.Tests.TestData;

namespace Workflows.Runner.Tests.TestWorkflows
{
    public class SubWorkflowTestWorkflow : WorkflowContainer
    {
        public List<string> ExecutionLog { get; } = new();

        public override async IAsyncEnumerable<Wait> Run()
        {
            ExecutionLog.Add("Parent: Start");

            // Wait for initial signal
            yield return WaitSignal<OrderReceivedSignal>("OrderReceived", "Initial order")
                .WithState("ParentState")
                .AfterMatch((signal, state) =>
                {
                    ExecutionLog.Add($"Parent: Order received - {signal.OrderId}");
                });

            // Execute sub-workflow (resumable function)
            yield return WaitSubWorkflow(
                ProcessOrderSubWorkflow(),
                "ProcessOrder",
                "Process order items");

            ExecutionLog.Add("Parent: Sub-workflow completed");

            // Another sub-workflow with state
            yield return WaitSubWorkflow(
                ShipmentSubWorkflow(),
                "Shipment",
                "Handle shipment")
                .WithState("ShipmentState");

            ExecutionLog.Add("Parent: End");
        }

        private async IAsyncEnumerable<Wait> ProcessOrderSubWorkflow()
        {
            ExecutionLog.Add("SubWorkflow1: Start");

            // Sub-workflow can have its own waits
            yield return ExecuteCommand<ReserveInventoryCommand, ReserveInventoryResult>(
                "ReserveInventory",
                new ReserveInventoryCommand { ProductId = "SUB-PROD1", Quantity = 1 })
                .OnResult((result) =>
                {
                    ExecutionLog.Add($"SubWorkflow1: Inventory reserved - {result.ReservationId}");
                });

            yield return WaitSignal<PaymentConfirmedSignal>("PaymentConfirmed", "Sub payment wait")
                .AfterMatch((signal) =>
                {
                    ExecutionLog.Add($"SubWorkflow1: Payment confirmed - {signal.TransactionId}");
                });

            ExecutionLog.Add("SubWorkflow1: End");
        }

        private async IAsyncEnumerable<Wait> ShipmentSubWorkflow()
        {
            ExecutionLog.Add("SubWorkflow2: Start");

            // This sub-workflow uses groups
            var carrierA = WaitSignal<ShipmentSignal>("CarrierA_Quote", "Carrier A")
                .AfterMatch((signal) => ExecutionLog.Add($"SubWorkflow2: Carrier A quote - {signal.TrackingNumber}"));

            var carrierB = WaitSignal<ShipmentSignal>("CarrierB_Quote", "Carrier B")
                .AfterMatch((signal) => ExecutionLog.Add($"SubWorkflow2: Carrier B quote - {signal.TrackingNumber}"));

            // Wait for first quote
            yield return WaitGroup([
                (SignalWait<ShipmentSignal>)carrierA,
                (SignalWait<ShipmentSignal>)carrierB
            ], "CarrierQuotes")
            .MatchAny();

            ExecutionLog.Add("SubWorkflow2: Selected carrier");

            // Confirm shipment
            yield return WaitSignal<ShipmentSignal>("ShipmentConfirmed", "Confirm")
                .AfterMatch((signal) =>
                {
                    ExecutionLog.Add($"SubWorkflow2: Shipment confirmed - {signal.TrackingNumber}");
                });

            ExecutionLog.Add("SubWorkflow2: End");
        }
    }
}
