using System;

namespace InProcessSqliteSample
{
    // ---------------------------------------------------------
    // Signal and Command Payloads
    // ---------------------------------------------------------

    public class PaymentRequest : Workflows.Abstraction.Runner.IDeferredCommand<PaymentRequest, PaymentResult>
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }

        System.Linq.Expressions.Expression<Func<PaymentRequest, PaymentResult, bool>> Workflows.Abstraction.Runner.IDeferredCommand<PaymentRequest, PaymentResult>.MatchingFunction =>
            (input, result) => result.OrderId == input.OrderId;
    }
}
