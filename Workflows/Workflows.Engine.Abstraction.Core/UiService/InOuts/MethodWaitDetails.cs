using System;
using Workflows.Handler.InOuts;
namespace Workflows.Handler.UiService.InOuts
{
    public class MethodWaitDetails
    {
        public MethodWaitDetails(
            string name,
            long id,
            WaitStatus status,
            int workflowId,
            string workflowName,
            int instanceId,
            DateTime created,
            string mandatoryPart,
            MatchStatus matchStatus,
            ExecutionStatus instanceUpdateStatus,
            ExecutionStatus executionStatus,
            TemplateDisplay templateDisplay)
        {
            Name = name;
            Id = id;
            Status = status;
            WorkflowId = workflowId;
            WorkflowName = workflowName;
            InstanceId = instanceId;
            MatchExpression = templateDisplay.MatchExpression;
            Created = created;
            MandatoryPart = mandatoryPart;
            MandatoryPartExpression = templateDisplay.MandatoryPartExpression;
            MatchStatus = matchStatus;
            InstanceUpdateStatus = instanceUpdateStatus;
            ExecutionStatus = executionStatus;
        }
        public DateTime Created { get; }
        public ExecutionStatus ExecutionStatus { get; }
        public int WorkflowId { get; }
        public string WorkflowName { get; }
        public int InstanceId { get; }
        public ExecutionStatus InstanceUpdateStatus { get; }
        public string MandatoryPart { get; }
        public string MandatoryPartExpression { get; }
        public string MatchExpression { get; }
        public MatchStatus MatchStatus { get; }
        public string Name { get; }
        public long Id { get; }
        public WaitStatus Status { get; }
        public long? SignalId { get; set; }
        public string GroupName { get; set; }
    }
}
