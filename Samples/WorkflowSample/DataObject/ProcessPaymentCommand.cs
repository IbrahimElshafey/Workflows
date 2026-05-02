namespace WorkflowSample.DataObject
{
    public class ProcessPaymentCommand
    {
        public string OrderId { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; }
    }
}
