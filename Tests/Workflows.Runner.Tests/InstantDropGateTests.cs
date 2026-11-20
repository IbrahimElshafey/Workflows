using System;
using System.IO;
using FluentAssertions;
using Workflows.Tools.CLI;
using Xunit;

namespace Workflows.Runner.Tests
{
    /// <summary>
    /// Deployment Instant Drop terminates the old worker before its instances are drained, so it must stay
    /// behind an explicit opt-in until the versioning safety net has been proven in production.
    /// </summary>
    public class InstantDropGateTests
    {
        [Fact]
        public void IsInstantDropEnabled_ShouldBeOffByDefault()
        {
            WithEnvironment(null, () =>
            {
                DeploymentManifestGenerator.IsInstantDropEnabled(false).Should().BeFalse();
                DeploymentManifestGenerator.IsInstantDropEnabled(true).Should().BeTrue();
            });
        }

        [Fact]
        public void IsInstantDropEnabled_ShouldHonourEnvironmentVariable()
        {
            WithEnvironment("true", () =>
                DeploymentManifestGenerator.IsInstantDropEnabled(false).Should().BeTrue());

            WithEnvironment("false", () =>
                DeploymentManifestGenerator.IsInstantDropEnabled(false).Should().BeFalse());
        }

        [Fact]
        public void GenerateManifest_ShouldKeepTheOldWorkerUntilItDrains_WhenInstantDropIsNotOptedIn()
        {
            WithEnvironment(null, () =>
            {
                var manifest = ReadManifest(out _);

                manifest.Should().Contain("\"InstantDropEnabled\": false");
                manifest.Should().Contain("\"DropOldWorkerImmediately\": false");
            });
        }

        [Fact]
        public void GenerateManifest_ShouldDropTheOldWorker_WhenInstantDropIsOptedIn()
        {
            WithEnvironment(null, () =>
            {
                var manifest = ReadManifest(out _, allowInstantDrop: true);

                manifest.Should().Contain("\"InstantDropEnabled\": true");
                manifest.Should().Contain("\"DropOldWorkerImmediately\": true");
            });
        }

        private static string ReadManifest(out string path, bool allowInstantDrop = false)
        {
            var outputDir = Path.Combine(Path.GetTempPath(), "wf-manifest-" + Guid.NewGuid().ToString("N"));
            try
            {
                var assembly = typeof(DeploymentManifestGenerator).Assembly.Location;
                path = DeploymentManifestGenerator.GenerateManifest(assembly, assembly, outputDir, allowInstantDrop);
                return File.ReadAllText(path);
            }
            finally
            {
                if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
            }
        }

        private static void WithEnvironment(string? value, Action action)
        {
            var original = Environment.GetEnvironmentVariable(DeploymentManifestGenerator.InstantDropVariable);
            try
            {
                Environment.SetEnvironmentVariable(DeploymentManifestGenerator.InstantDropVariable, value);
                action();
            }
            finally
            {
                Environment.SetEnvironmentVariable(DeploymentManifestGenerator.InstantDropVariable, original);
            }
        }
    }
}