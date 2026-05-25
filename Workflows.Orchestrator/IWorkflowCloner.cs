using System;
using Workflows.Abstraction.DTOs;

namespace Workflows.Orchestrator
{
    public interface IWorkflowCloner
    {
        WorkflowStateDto CloneStateWithNewIds(WorkflowStateDto source, out Guid newTriggeringWaitId, Guid oldTriggeringWaitId);
    }
}
