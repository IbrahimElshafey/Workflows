using Workflows.Handler.InOuts;
using Workflows.Handler.UiService.InOuts;

namespace Workflows.MvcUi.DisplayObject
{
    public class WorkflowInstancesModel
    {
        public string WorkflowName { get; set; }
        public List<WorkflowInstanceInfo> Instances { get; set; }

        public int InProgressCount => Instances.Count(x => x.WorkflowInstance.Status == WorkflowInstanceStatus.InProgress);
        public int FailedCount => Instances.Count(x => x.WorkflowInstance.Status == WorkflowInstanceStatus.InError);
        public int CompletedCount => Instances.Count(x => x.WorkflowInstance.Status == WorkflowInstanceStatus.Completed);
    }
}