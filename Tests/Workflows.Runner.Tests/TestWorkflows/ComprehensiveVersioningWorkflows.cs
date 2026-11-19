using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace Workflows.Runner.Tests.TestWorkflows
{
    // =========================================================================
    // STATE POCO CONTRACTS
    // =========================================================================
    public class ComplexOrderState_V1
    {
        public Guid OrderId { get; set; }
        public decimal Amount { get; set; }
        public string CustomerEmail { get; set; } = "";
        public string Status { get; set; } = "Created";
    }

    public class ComplexOrderState_V2
    {
        public Guid OrderId { get; set; }
        public double Amount { get; set; }
        public string CustomerEmail { get; set; } = "";
        public int AmountInCents { get; set; }
        public string PriorityLevel { get; set; } = "Standard";
        public string Status { get; set; } = "Created";
    }

    public class PaymentSubState_V1
    {
        public string TransactionId { get; set; } = "";
        public string Status { get; set; } = "Pending";
    }

    public class PaymentSubState_V2
    {
        public string TransactionId { get; set; } = "";
        public string Provider { get; set; } = "Stripe";
        public int FeeInCents { get; set; }
        public string Status { get; set; } = "Pending";
    }

    // =========================================================================
    // WORKFLOW DEFINITIONS (V1 & V2)
    // =========================================================================

    [Workflow("ComplexOrderWorkflow", 1)]
    public class ComplexOrderWorkflow_V1 : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run(ComplexOrderState_V1 state)
        {
            state.Status = "AwaitingApproval";
            yield return WaitSignal<string>("ApprovalSignal", "ApprovalWait");

            state.Status = "AwaitingPayment";
            yield return WaitSignal<string>("PaymentSignal", "PaymentWait");

            state.Status = "Completed";
        }
    }

    [Workflow("ComplexOrderWorkflow", 2)]
    public class ComplexOrderWorkflow_V2 : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run(ComplexOrderState_V2 state)
        {
            // Step 1: NEW FraudCheck step in V2 (shifts subsequent yield ordinals!)
            state.Status = "AwaitingFraudCheck";
            yield return WaitSignal<string>("FraudCheckSignal", "FraudCheckWait");

            // Step 2: ApprovalWait (Shifted from Step 1 in V1 to Step 2 in V2)
            state.Status = "AwaitingApproval";
            yield return WaitSignal<string>("ApprovalSignal", "ApprovalWait");

            // Step 3: SubWorkflow call to PaymentSubWorkflow V2
            state.Status = "ProcessingPayment";
            yield return WaitSubWorkflow(ExecutePayment(new PaymentSubState_V2
            {
                TransactionId = $"TX-{state.OrderId:N}",
                Provider = "Stripe",
                FeeInCents = 150
            }), "ExecutePayment", "Execute payment subworkflow");

            state.Status = "Completed";
        }

        [SubWorkflow]
        private async IAsyncEnumerable<Wait> ExecutePayment(PaymentSubState_V2 subState)
        {
            subState.Status = "AwaitingPreAuth";
            yield return WaitSignal<string>("PreAuthSignal", "PreAuthWait");

            subState.Status = "AwaitingGatewayCallback";
            yield return WaitSignal<string>("GatewayCallbackSignal", "GatewayCallbackWait");

            subState.Status = "PaymentSuccess";
        }
    }

    [Workflow("PaymentSubWorkflow", 1)]
    public class PaymentSubWorkflowContainer_V1 : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run(PaymentSubState_V1 state)
        {
            yield return WaitSubWorkflow(ExecutePayment(state), "ExecutePayment", "Execute payment subworkflow");
        }

        [SubWorkflow]
        private async IAsyncEnumerable<Wait> ExecutePayment(PaymentSubState_V1 subState)
        {
            subState.Status = "AwaitingGatewayCallback";
            yield return WaitSignal<string>("GatewayCallbackSignal", "GatewayCallbackWait");

            subState.Status = "PaymentSuccess";
        }
    }
}
