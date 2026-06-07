using System;
using System.Collections.Generic;
using Workflows.Definition;
using Workflows.Primitives;

namespace InProcessSqliteSample
{
    // ---------------------------------------------------------
    // Workflow Definition
    // ---------------------------------------------------------

    [Workflow("OrderWorkflow", 1)]
    public sealed partial class OrderProcessingWorkflow : WorkflowContainer<OrderWorkflowState>
    {
        // Domain state — populated by the first generic signal, NOT from StartWorkflowAsync input.
        // The workflow starts with NO state; the first wait is generic (no MatchIf).
        public string OrderId { get; set; } = string.Empty;

        public string CustomerEmail { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public string ShippingAddress { get; set; } = string.Empty;

        // Processing state — set by subsequent state-aware waits
        public bool PaymentAuthorized { get; set; }

        public string TrackingCode { get; set; } = string.Empty;

        public string ErrorReason { get; set; } = string.Empty;

        public List<string> ExecutionLog { get; set; } = new();

        public override async IAsyncEnumerable<Wait> Run(OrderWorkflowState state)
        {
            ExecutionLog.Add("Workflow ready. Waiting for an order to be received.");

            // ---------------------------------------------------------------
            // Step 1: GENERIC first wait — no MatchIf, no state dependency.
            //   The workflow has just started; no domain state exists yet.
            //   This signal carries the order data and bootstraps the instance.
            //   Signal = "wait for something to happen" — here: wait for an order.
            // ---------------------------------------------------------------
            yield return WaitSignal<OrderReceivedSignal>("OrderReceived", "WaitOrderReceived")
                .MatchIf(s => s.Amount > 0)
                .AfterMatch(
                    sig =>
                    {
                        // Populate domain state from the incoming signal
                        OrderId = sig.OrderId;
                        CustomerEmail = sig.CustomerEmail;
                        Amount = sig.Amount;
                        ShippingAddress = sig.ShippingAddress;
                        ExecutionLog.Add($"Order received: {OrderId} for {CustomerEmail}, amount {Amount:C}.");
                    });

            // ---------------------------------------------------------------
            // Step 2: PARALLEL state-dependent signal waits.
            //   State (OrderId, CustomerEmail) now exists — MatchIf is valid here.
            //   Both signals must arrive before proceeding (MatchAll).
            // ---------------------------------------------------------------
            ExecutionLog.Add("Waiting for stock confirmation and customer verification.");

            var stockWait = WaitSignal<StockConfirmedSignal>("StockConfirmed", "WaitStock")
                .MatchIf(sig => sig.OrderId == OrderId)   // state exists — safe to filter
                .AfterMatch(
                    sig =>
                    {
                        if(sig.Status == "Available")
                        {
                            State.StockOk = true;
                            ExecutionLog.Add("Stock confirmed available.");
                        } else
                        {
                            ErrorReason = "OutOfStock";
                            ExecutionLog.Add($"Stock unavailable for order {OrderId}.");
                        }
                    });

            var customerWait = WaitSignal<CustomerVerifiedSignal>("CustomerVerified", "WaitCustomer")
                .MatchIf(sig => sig.CustomerEmail == CustomerEmail)   // state exists — safe to filter
                .AfterMatch(
                    sig =>
                    {
                        State.CustomerOk = sig.Verified;
                        ExecutionLog.Add(
                            sig.Verified ? "Customer verification succeeded." : "Customer verification failed.");
                        if(!sig.Verified)
                            ErrorReason = "Customer verification failed";
                    });

            yield return WaitGroup(
                [ (SignalWait<StockConfirmedSignal>)stockWait, (SignalWait<CustomerVerifiedSignal>)customerWait ],
                "ParallelVerification")
                .MatchAll();

            if(!state.StockOk || !state.CustomerOk)
            {
                ExecutionLog.Add(
                    $"Aborting before payment — StockOk={state.StockOk}, CustomerOk={state.CustomerOk}. Reason: {ErrorReason}");
                yield break;
            }

            // ---------------------------------------------------------------
            // Step 3: Authorize payment — Command (active action, NOT a passive wait).
            //   Only reached after BOTH verifications pass.
            // ---------------------------------------------------------------
            ExecutionLog.Add($"Verifications passed. Authorizing payment of {Amount:C}.");
            yield return ExecuteCommand<PaymentRequest, PaymentResult>(
                "AuthorizePayment",
                new PaymentRequest { OrderId = OrderId, Amount = Amount })
                .WithExecutionMode(CommandExecutionMode.Deferred)
                .OnResult(
                    result =>
                    {
                        PaymentAuthorized = result.Success;
                        ExecutionLog.Add(
                            result.Success ? $"Payment authorized. Tx: {result.TransactionId}" : "Payment declined.");
                        if(!result.Success)
                            ErrorReason = "Payment failed";
                    });

            if(!PaymentAuthorized)
            {
                ExecutionLog.Add("Aborting — payment was declined.");
                yield break;
            }

            // ---------------------------------------------------------------
            // Step 4: Ship the order.
            // ---------------------------------------------------------------
            ExecutionLog.Add("Payment confirmed. Dispatching shipping command.");
            yield return ExecuteCommand<ShipOrderCommand, ShipOrderResult>(
                "ShipOrder",
                new ShipOrderCommand { OrderId = OrderId, ShippingAddress = ShippingAddress })
                .WithExecutionMode(CommandExecutionMode.Deferred)
                .OnResult(
                    result =>
                    {
                        State.OrderShipped = result.Success;
                        TrackingCode = result.TrackingNumber;
                        ExecutionLog.Add(
                            result.Success ? $"Order shipped. Tracking: {TrackingCode}" : "Shipping failed.");
                        if(!result.Success)
                            ErrorReason = "Shipping failed";
                    });

            ExecutionLog.Add("Workflow completed successfully.");
        }
    }

    public class OrderWorkflowState
    {
        public bool StockOk { get; set; }
        public bool CustomerOk { get; set; }
        public bool OrderShipped { get; set; }
    }
}
