namespace WorkflowSample.DataObject
{
    public class PaymentProcessedEvent
    {
        public int OrderId { get; set; }
        public bool IsSuccessful { get; set; }
    }
}