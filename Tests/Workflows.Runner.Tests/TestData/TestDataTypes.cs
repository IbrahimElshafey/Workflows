namespace Workflows.Runner.Tests.TestData
{
    // Commands
    public record ReserveInventoryCommand : Workflows.Abstraction.Runner.IImmediateCommand<ReserveInventoryCommand, ReserveInventoryResult>
    {
        public string ProductId { get; init; } = "";
        public int Quantity { get; init; }
    }

    public record ReserveInventoryResult
    {
        public string ReservationId { get; init; } = "";
        public bool Success { get; init; }
    }

    public record ProcessPaymentCommand : Workflows.Abstraction.Runner.IDeferredCommand<ProcessPaymentCommand, ProcessPaymentResult>
    {
        public string OrderId { get; init; } = "";
        public decimal Amount { get; init; }

        System.Linq.Expressions.Expression<System.Func<ProcessPaymentCommand, ProcessPaymentResult, bool>> Workflows.Abstraction.Runner.IDeferredCommand<ProcessPaymentCommand, ProcessPaymentResult>.MatchingFunction =>
            (input, result) => result.Success;
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
        public TaskStatus Status { get; init; }= TaskStatus.Created;
        public Customer Customer { get; init; } = new Customer();
    }

    public record Customer
    {
        public CustomerProfile Profile { get; set; }
    }
    public record CustomerProfile
    {
        public string Id { get; init; } = "";

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
