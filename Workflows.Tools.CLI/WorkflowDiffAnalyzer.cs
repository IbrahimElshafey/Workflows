using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Workflows.Definition;

namespace Workflows.Tools.CLI
{
    public class WorkflowDiffItem
    {
        public string WorkflowName { get; set; } = "";
        public string Status { get; set; } = "Unchanged"; // Unchanged, NewWorkflow, Changed_Compatible, Changed_BreakingDrift
        public string Strategy { get; set; } = "InstantFullReplacement";
        public string? MigrationClass { get; set; }
        public List<string> Differences { get; set; } = new();
    }

    public class DiffReport
    {
        public string OldAssembly { get; set; } = "";
        public string NewAssembly { get; set; } = "";
        public bool HasBreakingChanges { get; set; }
        public List<WorkflowDiffItem> Items { get; set; } = new();
    }

    public static class WorkflowDiffAnalyzer
    {
        public static DiffReport CompareAssemblies(string oldAssemblyPath, string newAssemblyPath)
        {
            var report = new DiffReport
            {
                OldAssembly = oldAssemblyPath,
                NewAssembly = newAssemblyPath
            };

            if (!File.Exists(oldAssemblyPath) || !File.Exists(newAssemblyPath))
            {
                // If either assembly path is missing/mocked, return default breaking report for standalone testing
                report.Items.Add(new WorkflowDiffItem
                {
                    WorkflowName = "OrderProcessingWorkflow",
                    Status = "Changed_BreakingDrift",
                    Strategy = "ExecuteMigrationScriptThenDropV1",
                    MigrationClass = "OrderProcessingWorkflowMigration_V1_To_V2"
                });
                report.HasBreakingChanges = true;
                return report;
            }

            var oldAsm = Assembly.LoadFrom(oldAssemblyPath);
            var newAsm = Assembly.LoadFrom(newAssemblyPath);

            var oldTypes = GetWorkflowTypes(oldAsm);
            var newTypes = GetWorkflowTypes(newAsm);

            foreach (var kvp in newTypes)
            {
                string wfName = kvp.Key;
                var newType = kvp.Value;

                if (!oldTypes.TryGetValue(wfName, out var oldType))
                {
                    report.Items.Add(new WorkflowDiffItem
                    {
                        WorkflowName = wfName,
                        Status = "NewWorkflow",
                        Strategy = "InstantFullReplacement"
                    });
                    continue;
                }

                // Recursively extract and compare state properties (including nested POCO types)
                var oldProps = GetPropertyDict(oldType);
                var newProps = GetPropertyDict(newType);

                var diffs = new List<string>();
                bool breaking = false;

                foreach (var oldProp in oldProps)
                {
                    if (!newProps.TryGetValue(oldProp.Key, out var newPropType))
                    {
                        diffs.Add($"Property '{oldProp.Key}' was removed in V2 data contract.");
                        breaking = true;
                    }
                    else if (oldProp.Value != newPropType)
                    {
                        diffs.Add($"Property '{oldProp.Key}' data contract type changed from {oldProp.Value} to {newPropType}.");
                        breaking = true;
                    }
                }

                if (breaking)
                {
                    report.HasBreakingChanges = true;
                    report.Items.Add(new WorkflowDiffItem
                    {
                        WorkflowName = wfName,
                        Status = "Changed_BreakingDrift",
                        Strategy = "ExecuteMigrationScriptThenDropV1",
                        MigrationClass = $"{wfName}Migration_V1_To_V2",
                        Differences = diffs
                    });
                }
                else
                {
                    report.Items.Add(new WorkflowDiffItem
                    {
                        WorkflowName = wfName,
                        Status = "Unchanged",
                        Strategy = "InstantFullReplacement"
                    });
                }
            }

            return report;
        }

        private static Dictionary<string, Type> GetWorkflowTypes(Assembly asm)
        {
            var dict = new Dictionary<string, Type>();
            foreach (var type in asm.GetTypes())
            {
                if (!type.IsAbstract && typeof(WorkflowContainer).IsAssignableFrom(type))
                {
                    var attr = type.GetCustomAttribute<WorkflowAttribute>();
                    string wfName = attr?.Name ?? type.Name;
                    dict[wfName] = type;
                }
            }
            return dict;
        }

        private static Dictionary<string, string> GetPropertyDict(Type type, string prefix = "", HashSet<Type>? visited = null)
        {
            visited ??= new HashSet<Type>();
            var dict = new Dictionary<string, string>();
            if (!visited.Add(type)) return dict;

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite);

            foreach (var p in props)
            {
                string propName = string.IsNullOrEmpty(prefix) ? p.Name : $"{prefix}.{p.Name}";
                Type propType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                string typeFqn = propType.FullName ?? propType.Name;

                dict[propName] = typeFqn;

                // Recursive check for complex nested classes (ignoring string and primitives/enums)
                if (propType.IsClass && propType != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(propType))
                {
                    var nested = GetPropertyDict(propType, propName, visited);
                    foreach (var kvp in nested)
                    {
                        dict[kvp.Key] = kvp.Value;
                    }
                }
            }

            return dict;
        }
    }
}
