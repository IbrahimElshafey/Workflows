namespace WorkflowSample.DataObject
{
    public class OrderReceivedEvent
    {
        public int OrderId { get; set; }
        public string CustomerEmail { get; set; }
        public decimal Amount { get; set; }
    }
}
