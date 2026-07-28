using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Workflows.Hosting.InProcess;
using Workflows.Tools.CLI;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class OutofProcessWorkerTests
    {
        [Fact]
        public void WorkflowSchemaGenerator_ShouldGenerateSchemaFile()
        {
            // Arrange
            string asmPath = typeof(OutofProcessWorkerTests).Assembly.Location;
            string outDir = Path.Combine(Path.GetTempPath(), $"WF_Schema_Test_{Guid.NewGuid():N}");

            try
            {
                // Act
                string schemaFile = WorkflowSchemaGenerator.GenerateSchemaJson(asmPath, outDir);

                // Assert
                File.Exists(schemaFile).Should().BeTrue();
                string content = File.ReadAllText(schemaFile);
                content.Should().Contain("SxSWorkflow");
            }
            finally
            {
                if (Directory.Exists(outDir))
                    Directory.Delete(outDir, true);
            }
        }

        [Fact]
        public void MigrationBoilerplateGenerator_ShouldGenerateMigrationClass()
        {
            // Arrange
            string outDir = Path.Combine(Path.GetTempPath(), $"WF_Mig_Test_{Guid.NewGuid():N}");

            try
            {
                // Act
                string migFile = MigrationBoilerplateGenerator.GenerateMigrationClass("TestOrderWorkflow", 1, 2, outDir);

                // Assert
                File.Exists(migFile).Should().BeTrue();
                string content = File.ReadAllText(migFile);
                content.Should().Contain("TestOrderWorkflowMigration_V1_To_V2");
                content.Should().Contain("_new.Instance.OrderId = old.Instance.OrderId;");
            }
            finally
            {
                if (Directory.Exists(outDir))
                    Directory.Delete(outDir, true);
            }
        }

        [Fact]
        public void DeploymentManifestGenerator_ShouldGenerateDeploymentManifest()
        {
            // Arrange
            string outDir = Path.Combine(Path.GetTempPath(), $"WF_Manifest_Test_{Guid.NewGuid():N}");

            try
            {
                // Act
                string manifestFile = DeploymentManifestGenerator.GenerateManifest("V1.dll", "V2.dll", outDir);

                // Assert
                File.Exists(manifestFile).Should().BeTrue();
                string content = File.ReadAllText(manifestFile);
                content.Should().Contain("ExecuteMigrationScriptThenDropV1");
                content.Should().Contain("OrderProcessingWorkflow");
            }
            finally
            {
                if (Directory.Exists(outDir))
                    Directory.Delete(outDir, true);
            }
        }
    }
}
