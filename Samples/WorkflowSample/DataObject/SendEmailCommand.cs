namespace WorkflowSample.DataObject
{
    /// <summary>
    /// Example command types for external services
    /// </summary>
    public class SendEmailCommand : Workflows.Abstraction.Runner.IImmediateCommand<SendEmailCommand, SendEmailResult>
    {
        public string To { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
    }
}
