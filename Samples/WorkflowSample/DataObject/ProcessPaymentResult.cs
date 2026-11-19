namespace WorkflowSample.DataObject
{
    public class ProcessPaymentResult
    {
        public string OrderId { get; set; } = string.Empty;
        public string TransactionId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
