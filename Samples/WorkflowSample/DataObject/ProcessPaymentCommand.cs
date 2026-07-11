namespace WorkflowSample.DataObject
{
    public class ProcessPaymentCommand : Workflows.Abstraction.Runner.IDeferredCommand<ProcessPaymentCommand, ProcessPaymentResult>
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;

        System.Linq.Expressions.Expression<System.Func<ProcessPaymentCommand, ProcessPaymentResult, bool>> Workflows.Abstraction.Runner.IDeferredCommand<ProcessPaymentCommand, ProcessPaymentResult>.MatchingFunction =>
            (input, result) => result.OrderId == input.OrderId;
    }
}
