using System;
using System.IO;
using Newtonsoft.Json;

namespace Workflows.Tools.CLI
{
    public static class DeploymentManifestGenerator
    {
        /// <summary>
        /// Name of the environment variable that opts a deployment into Instant Drop.
        /// </summary>
        public const string InstantDropVariable = "WORKFLOWS_ALLOW_INSTANT_DROP";

        /// <summary>
        /// Instant Drop terminates the old worker as soon as the deployment completes and cannot be undone.
        /// It stays behind an explicit opt-in (the <c>allowInstantDrop</c> argument or the
        /// <see cref="InstantDropVariable"/> environment variable) until the migration safety net has proven
        /// itself on real deployments.
        /// </summary>
        public static bool IsInstantDropEnabled(bool allowInstantDrop)
        {
            if (allowInstantDrop) return true;

            var fromEnvironment = Environment.GetEnvironmentVariable(InstantDropVariable);
            return bool.TryParse(fromEnvironment, out var enabled) && enabled;
        }

        public static string GenerateManifest(string oldAssembly, string newAssembly, string outputDir, bool allowInstantDrop = false)
        {
            var diffReport = WorkflowDiffAnalyzer.CompareAssemblies(oldAssembly, newAssembly);
            var instantDropEnabled = IsInstantDropEnabled(allowInstantDrop);

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
                    // Never request an instant drop unless the feature flag is on: the supervisor falls back
                    // to SxS worker spin-up and drains the old worker when its instances reach zero.
                    DropOldWorkerImmediately = instantDropEnabled && !diffReport.HasBreakingChanges,
                    InstantDropEnabled = instantDropEnabled,
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
