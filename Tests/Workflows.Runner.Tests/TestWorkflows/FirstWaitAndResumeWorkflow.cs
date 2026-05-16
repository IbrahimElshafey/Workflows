using System.Collections.Generic;
using Workflows.Definition;
using Workflows.Runner.Tests.TestData;

namespace Workflows.Runner.Tests.TestWorkflows
{
    /// <summary>
    /// Test workflow for first wait and resume scenarios
    /// </summary>
    public class FirstWaitAndResumeWorkflow : WorkflowContainer
    {
        public List<string> ExecutionLog { get; } = new();
        public int ResumeCount { get; set; }

        public override async IAsyncEnumerable<Wait> Run()
        {
            ExecutionLog.Add("Execution1: Start");

            // First wait - initial suspension point
            yield return WaitSignal<OrderReceivedSignal>("OrderReceived", "First wait")
                .WithState(1000)
                .MatchIf((signal, minAmount) => signal.Amount > minAmount)
                .AfterMatch((signal, minAmount) =>
                {
                    ExecutionLog.Add($"Execution1: First wait completed - Order: {signal.OrderId}, MinAmount: {minAmount}");
                });

            ExecutionLog.Add("Execution2: After first resume");
            ResumeCount++;

            // Second wait - test resumption after state restoration
            yield return ExecuteCommand<ProcessPaymentCommand, ProcessPaymentResult>(
                "ProcessPayment",
                new ProcessPaymentCommand { OrderId = "ORD-001", Amount = 100 })
                .WithState("PaymentState")
                .OnResult((result, state) =>
                {
                    ExecutionLog.Add($"Execution2: Payment processed - TxId: {result.TransactionId}, State: {state}");
                });

            ExecutionLog.Add("Execution3: After second resume");
            ResumeCount++;

            // Third wait - complex group scenario after multiple resumes
            var signal1 = WaitSignal<PaymentConfirmedSignal>("Payment1", "Payment option 1")
                .AfterMatch((signal) => ExecutionLog.Add($"Execution3: Payment1 - {signal.TransactionId}"));

            var signal2 = WaitSignal<PaymentConfirmedSignal>("Payment2", "Payment option 2")
                .AfterMatch((signal) => ExecutionLog.Add($"Execution3: Payment2 - {signal.TransactionId}"));

            var groupWait = WaitGroup([
                (SignalWait<PaymentConfirmedSignal>)signal1,
                (SignalWait<PaymentConfirmedSignal>)signal2
            ], "PaymentGroup");

            groupWait.WithState(3000);
            yield return groupWait.MatchAny();

            ExecutionLog.Add("Execution4: After third resume");
            ResumeCount++;

            // Fourth wait - test resumption with delay
            yield return WaitDelay(TimeSpan.FromSeconds(30), "DelayWait", "30 second delay");

            ExecutionLog.Add("Execution5: After delay resume");
            ResumeCount++;

            // Final wait - ensure state is preserved across all resumes
            yield return WaitSignal<ShipmentSignal>("FinalShipment", "Final wait")
                .WithState(new { ResumeCount, FinalCheck = true })
                .AfterMatch((signal, state) =>
                {
                    ExecutionLog.Add($"Execution5: Final wait - Tracking: {signal.TrackingNumber}, Resumes: {ResumeCount}");
                });

            ExecutionLog.Add($"Execution6: Completed - Total resumes: {ResumeCount}");
        }
    }
}
