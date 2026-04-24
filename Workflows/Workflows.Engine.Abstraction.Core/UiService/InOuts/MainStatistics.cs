namespace Workflows.Handler.UiService.InOuts
{
    public class MainStatistics
    {
        public MainStatistics(
            int Services,
            int Workflows,
            int WorkflowsInstances,
            int MethodGroups,
            int Signals,
            int LatestLogErrors)
        {
            this.Services = Services;
            this.Workflows = Workflows;
            this.WorkflowsInstances = WorkflowsInstances;
            this.MethodGroups = MethodGroups;
            this.Signals = Signals;
            this.LatestLogErrors = LatestLogErrors;
        }

        public int Services { get; }
        public int Workflows { get; }
        public int WorkflowsInstances { get; }
        public int MethodGroups { get; }
        public int Signals { get; }
        public int LatestLogErrors { get; }
    }
}
