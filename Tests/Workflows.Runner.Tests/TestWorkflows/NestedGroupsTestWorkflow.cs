using System.Collections.Generic;
using Workflows.Definition;
using Workflows.Runner.Tests.TestData;

namespace Workflows.Runner.Tests.TestWorkflows
{
    public class NestedGroupsTestWorkflow : WorkflowContainer
    {
        public List<string> ExecutionLog { get; } = new();

        public override async IAsyncEnumerable<Wait> Run()
        {
            ExecutionLog.Add("Start");

            // Inner group 1: Payment signals
            var paymentGroup = WaitGroup([
                (SignalWait<PaymentConfirmedSignal>)WaitSignal<PaymentConfirmedSignal>("PaymentConfirmed", "Payment1")
                    .AfterMatch((signal) => ExecutionLog.Add($"Payment1: {signal.TransactionId}")),

                (SignalWait<PaymentConfirmedSignal>)WaitSignal<PaymentConfirmedSignal>("PaymentBackup", "Payment2")
                    .AfterMatch((signal) => ExecutionLog.Add($"Payment2: {signal.TransactionId}"))
            ], "PaymentGroup")
            .MatchAny(); // First payment wins

            // Inner group 2: Shipment signals
            var shipmentGroup = WaitGroup([
                (SignalWait<ShipmentSignal>)WaitSignal<ShipmentSignal>("ShipmentReady", "Shipment1")
                    .AfterMatch((signal) => ExecutionLog.Add($"Shipment1: {signal.TrackingNumber}")),

                (SignalWait<ShipmentSignal>)WaitSignal<ShipmentSignal>("ShipmentBackup", "Shipment2")
                    .AfterMatch((signal) => ExecutionLog.Add($"Shipment2: {signal.TrackingNumber}"))
            ], "ShipmentGroup")
            .MatchAny(); // First shipment wins

            // Outer group: Wait for both payment AND shipment
            yield return WaitGroup([
                (GroupWait)paymentGroup,
                (GroupWait)shipmentGroup
            ], "OrderCompletionGroup")
            .MatchAll(); // Both groups must complete

            ExecutionLog.Add("Both payment and shipment completed");

            // Another nested scenario: Group of groups with multiple signals
            var wh1item1 = (SignalWait<ShipmentSignal>)WaitSignal<ShipmentSignal>("Warehouse1Item1", "WH1-Item1")
                    .AfterMatch((signal) => ExecutionLog.Add($"WH1-Item1: {signal.TrackingNumber}"));
            var wh1item2 = (SignalWait<ShipmentSignal>)WaitSignal<ShipmentSignal>("Warehouse1Item2", "WH1-Item2")
                    .AfterMatch((signal) => ExecutionLog.Add($"WH1-Item2: {signal.TrackingNumber}"));

            var warehouse1 = WaitGroup([
                wh1item1,
                wh1item2
            ], "Warehouse1Group")
            .MatchAll();

            var wh2item1 = (SignalWait<ShipmentSignal>)WaitSignal<ShipmentSignal>("Warehouse2Item1", "WH2-Item1")
                    .AfterMatch((signal) => ExecutionLog.Add($"WH2-Item1: {signal.TrackingNumber}"));

            var warehouse2 = WaitGroup([
                wh2item1
            ], "Warehouse2Group")
            .MatchAll();

            // Wait for ANY warehouse to fulfill the order
            yield return WaitGroup([
                (GroupWait)warehouse1,
                (GroupWait)warehouse2
            ], "WarehouseFulfillmentGroup")
            .MatchAny(); // First warehouse to complete all items wins

            ExecutionLog.Add("Order fulfilled from one warehouse");
            ExecutionLog.Add("End");
        }
    }
}
