using System;
using System.Collections.Concurrent;

namespace Workflows.Definition.Registration
{
    public static class WorkflowDefinitionRegistry
    {
        // Key => Workflow Name, Value => (WorkflowContainer Type, StateMachine Type, StateType Type)
        public static ConcurrentDictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType)> Workflows { get; } = new();
    }
}
