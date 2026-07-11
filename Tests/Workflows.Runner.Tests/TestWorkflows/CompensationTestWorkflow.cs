using Workflows.Definition;
using Workflows.Runner.Tests.TestData;

namespace Workflows.Runner.Tests.TestWorkflows
{
    [Workflow("CompensationTestWorkflow", 1)]
    public sealed class CompensationTestWorkflow : WorkflowContainer
    {
        public List<string> ExecutionLog { get; set; } = new();

        public async IAsyncEnumerable<Wait> Run()
        {
            ExecutionLog.Add("Start");

            // Step 1: Execute first command with compensation
            yield return ExecuteImmediate<ReserveInventoryCommand, ReserveInventoryResult>(
                "ReserveInventory",
                new ReserveInventoryCommand { ProductId = "PROD-001", Quantity = 5 })
                .WithState("InventoryReservation")
                .WithToken("OrderSaga")
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
            yield return ExecuteDeferred<ProcessPaymentCommand, ProcessPaymentResult>(
                "ProcessPayment",
                new ProcessPaymentCommand { OrderId = "ORD-123", Amount = 100 })
                .WithState("PaymentProcessing")
                .WithToken("OrderSaga", "PaymentScope")
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

