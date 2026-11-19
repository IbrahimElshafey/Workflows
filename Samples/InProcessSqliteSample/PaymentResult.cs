using System;

namespace InProcessSqliteSample
{
    public class PaymentResult
    {
        public string OrderId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string TransactionId { get; set; } = string.Empty;
    }
}
