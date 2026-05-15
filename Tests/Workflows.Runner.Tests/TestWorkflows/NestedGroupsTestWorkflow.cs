using System.Collections.Generic;
using Workflows.Definition;
using Workflows.Runner.Tests.TestData;

namespace Workflows.Runner.Tests.TestWorkflows
{
    public class NestedGroupsTestWorkflow : WorkflowContainer
    {
        public List<string> ExecutionLog { get; } = new();

        public override async IAsyncEnumerable<Wait> ExecuteWorkflowAsync()
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

            // Another nested scenario: Group of groups with commands
            var warehouse1 = WaitGroup([
                (CommandWait<ReserveInventoryCommand, ReserveInventoryResult>)
                    ExecuteCommand<ReserveInventoryCommand, ReserveInventoryResult>(
                        "ReserveInventory",
                        new ReserveInventoryCommand { ProductId = "WH1-PROD1", Quantity = 2 })
                    .OnResult((result) => ExecutionLog.Add($"WH1-Item1: {result.ReservationId}")),

                (CommandWait<ReserveInventoryCommand, ReserveInventoryResult>)
                    ExecuteCommand<ReserveInventoryCommand, ReserveInventoryResult>(
                        "ReserveInventory",
                        new ReserveInventoryCommand { ProductId = "WH1-PROD2", Quantity = 3 })
                    .OnResult((result) => ExecutionLog.Add($"WH1-Item2: {result.ReservationId}"))
            ], "Warehouse1Group")
            .MatchAll();

            var warehouse2 = WaitGroup([
                (CommandWait<ReserveInventoryCommand, ReserveInventoryResult>)
                    ExecuteCommand<ReserveInventoryCommand, ReserveInventoryResult>(
                        "ReserveInventory",
                        new ReserveInventoryCommand { ProductId = "WH2-PROD1", Quantity = 1 })
                    .OnResult((result) => ExecutionLog.Add($"WH2-Item1: {result.ReservationId}"))
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
