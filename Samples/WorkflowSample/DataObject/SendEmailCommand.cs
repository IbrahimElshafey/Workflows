namespace WorkflowSample.DataObject
{
    /// <summary>
    /// Example command types for external services
    /// </summary>
    public class SendEmailCommand
    {
        public string To { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
    }
}
