using System;
using System.Collections.Generic;
using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Abstraction.DTOs
{
    /// <summary>
    /// Result of a workflow run operation containing execution status and state information.
    /// </summary>
    //Todo: unify with WorkflowExecutionResult and consider renaming to WorkflowRunOutcome or similar for clarity.
    public class WorkflowExecutionResponse
    {
        public WorkflowStateDto UpdatedState { get; set; } // The new JSON snapshot
        public List<Guid> NewWaitsIds { get; set; } // To be indexed in SQL
        public List<Guid> ConsumedWaitsIds { get; set; } // To be removed from SQL
        public string ExecutionCode { get; internal set; }
    }
}
