using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Workflows.Abstraction.Runner;

namespace Workflows.Runner.Tests.Infrastructure
{
    /// <summary>
    /// In-memory workflow registry for testing
    /// </summary>
    internal class InMemoryWorkflowRegistry : IWorkflowRegistry
    {
        private readonly Dictionary<(string Name, int Version), (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod)> _versionedWorkflows = new();

        public Dictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod)> Workflows
        {
            get
            {
                var latest = new Dictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod)>();
                foreach (var kvp in _versionedWorkflows)
                {
                    if (!latest.TryGetValue(kvp.Key.Name, out var existingTuple) || kvp.Key.Version > GetVersionFromTuple(existingTuple))
                    {
                        latest[kvp.Key.Name] = kvp.Value;
                    }
                }
                return latest;
            }
        }

        public Dictionary<string, Type> SignalTypes { get; } = new();
        public Dictionary<string, (Type CommandPayloadType, Type CommandResultType)> CommandTypes { get; } = new();

        public bool TryGetWorkflow(string name, int version, out (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod) tuple)
        {
            return _versionedWorkflows.TryGetValue((name, version), out tuple);
        }

        public bool TryGetLatestWorkflow(string name, out (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod) tuple)
        {
            tuple = default;
            var bestVersion = -1;
            foreach (var kvp in _versionedWorkflows)
            {
                if (kvp.Key.Name == name && kvp.Key.Version > bestVersion)
                {
                    bestVersion = kvp.Key.Version;
                    tuple = kvp.Value;
                }
            }
            return bestVersion >= 0;
        }

        public void AddWorkflow(string name, int version, (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod) tuple)
        {
            _versionedWorkflows[(name, version)] = tuple;
        }

        private static int GetVersionFromTuple((Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod) tuple)
        {
            var attr = tuple.WorkflowContainer.GetCustomAttribute<Workflows.Definition.WorkflowAttribute>();
            return attr?.Version ?? 1;
        }
    }
}
