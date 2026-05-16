using System;
using System.Collections.Generic;
using Workflows.Abstraction.Runner;

namespace Workflows.Runner.Tests.Infrastructure
{
    /// <summary>
    /// In-memory workflow registry for testing
    /// </summary>
    internal class InMemoryWorkflowRegistry : IWorkflowRegistry
    {
        public Dictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine)> Workflows { get; } = new();
        public Dictionary<string, Type> SignalTypes { get; } = new();
        public Dictionary<string, (Type CommandPayloadType, Type CommandResultType)> CommandTypes { get; } = new();
    }
}
