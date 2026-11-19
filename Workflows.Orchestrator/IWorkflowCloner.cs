using System;
using Workflows.Abstraction.DTOs;

namespace Workflows.Orchestrator
{
    public interface IWorkflowCloner
    {
        WorkflowStateDto CloneStateWithNewIds(WorkflowStateDto source, out string newTriggeringWaitId, string oldTriggeringWaitId);
    }
}
