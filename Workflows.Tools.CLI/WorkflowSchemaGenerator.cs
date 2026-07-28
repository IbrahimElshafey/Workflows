using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Workflows.Definition;

namespace Workflows.Tools.CLI
{
    public static class WorkflowSchemaGenerator
    {
        public static string GenerateSchemaJson(string assemblyPath, string outputDir)
        {
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException($"Assembly not found: {assemblyPath}");
            }

            var asm = Assembly.LoadFrom(assemblyPath);
            var workflowTypes = asm.GetTypes()
                .Where(t => !t.IsAbstract && typeof(WorkflowContainer).IsAssignableFrom(t))
                .ToList();

            var schemaList = new List<object>();

            foreach (var type in workflowTypes)
            {
                var attr = type.GetCustomAttribute<WorkflowAttribute>();
                string wfName = attr?.Name ?? type.Name;
                int wfVersion = attr?.Version ?? 1;

                var stateProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && p.CanWrite)
                    .Select(p => new
                    {
                        Name = p.Name,
                        TypeFqn = p.PropertyType.FullName ?? p.PropertyType.Name,
                        Nullable = Nullable.GetUnderlyingType(p.PropertyType) != null
                    }).ToList();

                schemaList.Add(new
                {
                    WorkflowName = wfName,
                    Version = wfVersion,
                    StateClass = type.FullName,
                    Properties = stateProps
                });
            }

            var manifest = new
            {
                Assembly = asm.GetName().Name,
                Version = asm.GetName().Version?.ToString() ?? "1.0.0",
                GeneratedUtc = DateTime.UtcNow,
                Workflows = schemaList
            };

            Directory.CreateDirectory(outputDir);
            string filePath = Path.Combine(outputDir, $"{asm.GetName().Name}_Schema.json");
            File.WriteAllText(filePath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
            return filePath;
        }
    }
}
