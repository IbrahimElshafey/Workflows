using System;

namespace InProcessSqliteSample
{
    // ---------------------------------------------------------
    // Signal and Command Payloads
    // ---------------------------------------------------------

    public class PaymentRequest
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
