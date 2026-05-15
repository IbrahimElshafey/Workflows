using System.Collections.Generic;
using Workflows.Definition;
using Workflows.Runner.Tests.TestData;

namespace Workflows.Runner.Tests.TestWorkflows
{
    public class CompensationTestWorkflow : WorkflowContainer
    {
        public List<string> ExecutionLog { get; } = new();

        public override async IAsyncEnumerable<Wait> ExecuteWorkflowAsync()
        {
            ExecutionLog.Add("Start");

            // Step 1: Execute first command with compensation
            yield return ExecuteCommand<ReserveInventoryCommand, ReserveInventoryResult>(
                "ReserveInventory",
                new ReserveInventoryCommand { ProductId = "PROD-001", Quantity = 5 })
                .WithState("InventoryReservation")
                .WithTokens("OrderSaga")
                .OnResult((result, state) =>
                {
                    ExecutionLog.Add($"Inventory reserved: {result.ReservationId}");
                })
                .RegisterCompensation((result, state) =>
                {
                    ExecutionLog.Add($"Compensating inventory: {result.ReservationId}");
                    return ValueTask.CompletedTask;
                });

            // Step 2: Execute payment command with compensation
            yield return ExecuteCommand<ProcessPaymentCommand, ProcessPaymentResult>(
                "ProcessPayment",
                new ProcessPaymentCommand { OrderId = "ORD-123", Amount = 100 })
                .WithState("PaymentProcessing")
                .WithTokens("OrderSaga", "PaymentScope")
                .OnResult((result, state) =>
                {
                    ExecutionLog.Add($"Payment processed: {result.TransactionId}");
                })
                .RegisterCompensation((result, state) =>
                {
                    ExecutionLog.Add($"Refunding payment: {result.TransactionId}");
                    return ValueTask.CompletedTask;
                });

            // Step 3: Simulate failure point
            if (ShouldFail)
            {
                ExecutionLog.Add("Failure detected - triggering compensation");
                yield return Compensate("OrderSaga");
            }

            ExecutionLog.Add("End");
        }

        public bool ShouldFail { get; set; }
    }
}
