using System;
using System.IO;
using Newtonsoft.Json;

namespace Workflows.Tools.CLI
{
    public static class DeploymentManifestGenerator
    {
        public static string GenerateManifest(string oldAssembly, string newAssembly, string outputDir)
        {
            var diffReport = WorkflowDiffAnalyzer.CompareAssemblies(oldAssembly, newAssembly);

            var manifest = new
            {
                DeploymentId = $"dep_{DateTime.UtcNow:yyyyMMdd_HHmmss}",
                SourceAssembly = Path.GetFileName(newAssembly),
                TargetVersion = 2,
                CreatedUtc = DateTime.UtcNow,
                HasBreakingChanges = diffReport.HasBreakingChanges,
                Workflows = diffReport.Items,
                SupervisorInstruction = new
                {
                    DropOldWorkerImmediately = !diffReport.HasBreakingChanges,
                    TargetWorkerVersion = "2.0.0"
                }
            };

            Directory.CreateDirectory(outputDir);
            string filePath = Path.Combine(outputDir, "deployment-manifest.json");
            File.WriteAllText(filePath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
            return filePath;
        }
    }
}
