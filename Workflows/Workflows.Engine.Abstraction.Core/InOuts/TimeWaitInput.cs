namespace Workflows.Handler.InOuts
{
    public class TimeWaitInput
    {
        public string TimeMatchId { get; set; }
        public string Description { get; set; }
        public int RequestedByWorkflowId { get; set; }

        public override string ToString()
        {
            return Description;
        }
    }
}
