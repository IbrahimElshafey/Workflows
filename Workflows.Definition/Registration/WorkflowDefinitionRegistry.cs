using System;
using System.Collections.Concurrent;

namespace Workflows.Definition.Registration
{
    public static class WorkflowDefinitionRegistry
    {
        // Key => Workflow Name, Value => (WorkflowContainer Type, StateMachine Type, StateType Type, StartMethod Name)
        public static ConcurrentDictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod)> Workflows { get; } = new();
    }
}
