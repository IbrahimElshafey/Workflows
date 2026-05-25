using System;

namespace InProcessSqliteSample
{
    // ---------------------------------------------------------
    // Workflow Definition
    // ---------------------------------------------------------

    // New: generic trigger signal that starts the order workflow
    public class OrderReceivedSignal
    {
        public string OrderId { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string ShippingAddress { get; set; } = string.Empty;
    }
}
