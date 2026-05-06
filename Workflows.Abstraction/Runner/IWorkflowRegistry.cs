using System;
using System.Collections.Generic;

namespace Workflows.Abstraction.Runner
{
    public interface IWorkflowRegistry
    {
        Dictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine)> Workflows { get; }
        Dictionary<string, Type> SignalTypes { get; }
        Dictionary<string, (Type CommandPayloadType, Type CommandResultType)> CommandTypes { get; }
    }
}
