using System;
using System.Collections.Generic;

namespace Workflows.Abstraction.Runner
{
    public interface IWorkflowRegistry
    {
        /// <summary>
        /// Backward-compatible dictionary returning the latest version of each workflow by name.
        /// </summary>
        Dictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod)> Workflows { get; }
        Dictionary<string, Type> SignalTypes { get; }
        Dictionary<string, (Type CommandPayloadType, Type CommandResultType)> CommandTypes { get; }

        /// <summary>
        /// Tries to get a workflow by exact name and version.
        /// </summary>
        bool TryGetWorkflow(string name, int version, out (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod) tuple);

        /// <summary>
        /// Tries to get the latest registered version of a workflow by name.
        /// </summary>
        bool TryGetLatestWorkflow(string name, out (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod) tuple);
    }
}