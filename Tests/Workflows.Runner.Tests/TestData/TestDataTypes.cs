namespace Workflows.Runner.Tests.TestData
{
    // Commands
    public record ReserveInventoryCommand
    {
        public string ProductId { get; init; } = "";
        public int Quantity { get; init; }
    }

    public record ReserveInventoryResult
    {
        public string ReservationId { get; init; } = "";
        public bool Success { get; init; }
    }

    public record ProcessPaymentCommand
    {
        public string OrderId { get; init; } = "";
        public decimal Amount { get; init; }
    }

    public record ProcessPaymentResult
    {
        public string TransactionId { get; init; } = "";
        public bool Success { get; init; }
    }

    // Signals
    public record OrderReceivedSignal
    {
        public string OrderId { get; init; } = "";
        public decimal Amount { get; init; }
    }

    public record PaymentConfirmedSignal
    {
        public string TransactionId { get; init; } = "";
        public decimal Amount { get; init; }
    }

    public record ShipmentSignal
    {
        public string TrackingNumber { get; init; } = "";
        public string Carrier { get; init; } = "";
    }
}
