using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Workflows.Definition.Registration
{
    public static class WorkflowDefinitionRegistry
    {
        // Key => (Workflow Name, Version), Value => (WorkflowContainer Type, StateMachine Type, StateType Type, StartMethod Name)
        private static readonly ConcurrentDictionary<(string Name, int Version), (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod)> _versionedWorkflows = new();

        /// <summary>
        /// Backward-compatible property returning the latest version of each workflow by name.
        /// </summary>
        public static ConcurrentDictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod)> Workflows
        {
            get
            {
                var latest = new ConcurrentDictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod)>();
                foreach (var kvp in _versionedWorkflows)
                {
                    var name = kvp.Key.Name;
                    var version = kvp.Key.Version;
                    if (!latest.TryGetValue(name, out var existing) || version > GetVersionFromTuple(existing))
                    {
                        latest[name] = kvp.Value;
                    }
                }
                return latest;
            }
        }

        /// <summary>
        /// Tries to get a workflow by exact name and version.
        /// </summary>
        public static bool TryGetWorkflow(string name, int version, out (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod) tuple)
        {
            return _versionedWorkflows.TryGetValue((name, version), out tuple);
        }

        /// <summary>
        /// Tries to get the latest registered version of a workflow by name.
        /// </summary>
        public static bool TryGetLatestWorkflow(string name, out (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod) tuple)
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

        /// <summary>
        /// Gets all registered versions for a given workflow name.
        /// </summary>
        public static IReadOnlyList<int> GetAllVersions(string name)
        {
            return _versionedWorkflows.Keys
                .Where(k => k.Name == name)
                .Select(k => k.Version)
                .OrderBy(v => v)
                .ToList();
        }

        /// <summary>
        /// Internal setter for the versioned dictionary. Used by WorkflowBuilder during registration.
        /// </summary>
        internal static void AddOrUpdate(string name, int version, (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod) tuple)
        {
            _versionedWorkflows[(name, version)] = tuple;
        }

        private static int GetVersionFromTuple((Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod) tuple)
        {
            var attr = tuple.WorkflowContainer.GetCustomAttribute<WorkflowAttribute>();
            return attr?.Version ?? 1;
        }
    }
}
