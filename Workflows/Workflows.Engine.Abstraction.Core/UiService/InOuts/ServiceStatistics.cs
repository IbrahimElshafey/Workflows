namespace Workflows.Handler.UiService.InOuts
{
    public class ServiceStatistics
    {
        public ServiceStatistics(int Id, string ServiceName, int ErrorCounter, int WorkflowsCount, int MethodsCount)
        {
            this.Id = Id;
            this.ServiceName = ServiceName;
            this.ErrorCounter = ErrorCounter;
            this.WorkflowsCount = WorkflowsCount;
            this.MethodsCount = MethodsCount;
        }

        public int Id { get; }
        public string ServiceName { get; }
        public int ErrorCounter { get; }
        public int WorkflowsCount { get; }
        public int MethodsCount { get; }
    }
}
