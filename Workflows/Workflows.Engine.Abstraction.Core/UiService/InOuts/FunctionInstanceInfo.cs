using System;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
namespace Workflows.Handler.UiService.InOuts
{
    public class WorkflowInstanceInfo
    {
        public WorkflowInstanceInfo(WorkflowInstance WorkflowInstance, WaitEntity CurrentWait, int WaitsCount, int Id)
        {
            this.WorkflowInstance = WorkflowInstance;
            this.CurrentWait = CurrentWait;
            this.WaitsCount = WaitsCount;
            this.Id = Id;
        }
        public WorkflowInstance WorkflowInstance { get; private set; }
        public WaitEntity CurrentWait { get; private set; }
        public int WaitsCount { get; private set; }
        public int Id { get; private set; }
        public string StateColor => WorkflowInstance.Status switch
        {
            WorkflowInstanceStatus.New => "black",
            WorkflowInstanceStatus.InProgress => "yellow",
            WorkflowInstanceStatus.Completed => "green",
            WorkflowInstanceStatus.InError => "red",
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}
