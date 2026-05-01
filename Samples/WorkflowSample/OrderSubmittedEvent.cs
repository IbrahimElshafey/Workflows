namespace Workflows.Sample
{
    // --- Sample Signal Data Payloads ---
    public class OrderSubmittedEvent
    {
        public int OrderId { get; set; }
        public string CustomerName { get; set; }
    }
}