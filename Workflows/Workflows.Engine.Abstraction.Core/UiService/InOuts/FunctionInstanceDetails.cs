using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.Collections;
using System;
using System.Collections.Generic;
namespace Workflows.Handler.UiService.InOuts
{
    public class WorkflowInstanceDetails
    {
        public string WorkflowUrn { get; }
        public string WorkflowName { get; }
        public int InstanceId { get; }
        public WorkflowInstanceStatus Status { get; }
        public string InstanceData { get; }
        public DateTime Created { get; }
        public DateTime Modified { get; }
        public int ErrorsCount { get; }
        public ArrayList Waits { get; }
        public List<MethodWaitDetails> MethodWaitDetails { get; }
        public List<LogRecord> Logs { get; }
        public int WorkflowId { get; }

        public WorkflowInstanceDetails(
            int instanceId, int workflowId, string name, string workflowName, WorkflowInstanceStatus status, string instanceData, DateTime created, DateTime modified, int errorsCount, ArrayList waits, List<LogRecord> logs)
        {
            InstanceId = instanceId;
            WorkflowId = workflowId;
            WorkflowUrn = name;
            WorkflowName = workflowName;
            Status = status;
            InstanceData = instanceData;
            Created = created;
            Modified = modified;
            ErrorsCount = errorsCount;
            Waits = waits;
            Logs = logs;
        }
    }
}
