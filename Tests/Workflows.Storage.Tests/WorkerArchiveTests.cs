using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workflows.Hosting.InProcess;
using Workflows.Storage.EntityFrameworkCore;
using Xunit;

using Workflows.Runner.Tests.Infrastructure;

namespace Workflows.Storage.Tests
{
    /// <summary>
    /// Retired worker builds are archived so a late compensation can still respawn the version that produced it.
    /// </summary>
    public class WorkerArchiveTests : IDisposable
    {
        private readonly string _root;

        public WorkerArchiveTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "wf-archive-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Best effort - a locked file must not fail the suite.
            }
        }

        [Fact]
        public async Task ArchiveAsync_ShouldCopyThePublishedBuildAndRecordMetadata()
        {
            var publish = CreateFakePublish("1.0.0", "Workflows.Runner.dll");
            var store = new WorkerArchiveStore(Path.Combine(_root, "archive"));

            var archived = await store.ArchiveAsync("1.0.0", publish, runtimeIdentifier: "win-x64");

            archived.Version.Should().Be("1.0.0");
            archived.ExecutableName.Should().Be("Workflows.Runner.dll");
            archived.RuntimeIdentifier.Should().Be("win-x64");
            File.Exists(archived.ExecutablePath).Should().BeTrue();
            File.Exists(Path.Combine(archived.Directory, "sub", "runtimeconfig.json")).Should().BeTrue(
                "the archive must be self-contained, including nested publish folders");

            var found = store.FindArchivedWorker("1.0.0");
            found.Should().NotBeNull();
            found!.ExecutablePath.Should().Be(archived.ExecutablePath);
            found.SourceDirectory.Should().Be(publish);

            store.ListArchivedWorkers().Should().ContainSingle().Which.Version.Should().Be("1.0.0");
            store.FindArchivedWorker("9.9.9").Should().BeNull();
        }

        [Fact]
        public async Task ArchiveAsync_ShouldPreferTheExecutableEntryPointWhenPresent()
        {
            var publish = CreateFakePublish("2.0.0", "Workflows.Runner.exe");
            var store = new WorkerArchiveStore(Path.Combine(_root, "archive"));

            var archived = await store.ArchiveAsync("2.0.0", publish);

            archived.ExecutableName.Should().Be("Workflows.Runner.exe");
        }

        [Fact]
        public async Task WorkerProcessSupervisor_ShouldPreferTheArchivedBuildWhenRespawning()
        {
            var archiveRoot = Path.Combine(_root, "archive");
            var archive = new WorkerArchiveStore(archiveRoot);
            var publish = CreateFakePublish("1.0.0", "Workflows.Runner.dll");
            await archive.ArchiveAsync("1.0.0", publish);

            var livePath = Path.Combine(_root, "live", "Workflows.Runner.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(livePath)!);
            File.WriteAllText(livePath, "live-worker");
            var supervisor = new WorkerProcessSupervisor(livePath, archive);

            supervisor.ResolveWorkerExecutable("1.0.0")
                .Should().Be(Path.Combine(archiveRoot, "1.0.0", "Workflows.Runner.dll"),
                    "the retired version must run on its own archive, not on the host runtime");
            supervisor.ResolveWorkerExecutable("2.0.0").Should().Be(livePath);

            (await supervisor.TryArchiveWorkerAsync("1.0.0")).Should().BeTrue();
            File.Exists(Path.Combine(archiveRoot, "1.0.0", "Workflows.Runner.dll")).Should().BeTrue(
                "archiving an already archived version must not delete the only copy");
        }

        [Fact]
        public async Task WorkerProcessSupervisor_ShouldReportFailureWhenArchivingIsDisabled()
        {
            var supervisor = new WorkerProcessSupervisor(Path.Combine(_root, "Workflows.Runner.dll"));

            supervisor.ArchiveStore.Should().BeNull();
            (await supervisor.TryArchiveWorkerAsync("1.0.0")).Should().BeFalse();
        }

        [Fact]
        public async Task WorkerDrainWorker_ShouldArchiveTheBuildBeforeTerminatingTheWorker()
        {
            using var connection = new SqliteConnection($"Data Source=InMemory_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            connection.Open();
            var options = new DbContextOptionsBuilder<WorkflowsDbContext>().UseSqlite(connection).Options;
            using var context = new WorkflowsDbContext(options);
            context.Database.EnsureCreated();

            var store = new WorkflowStore(context, new TestObjectSerializer());
            await store.RegisterWorkerCapabilitiesAsync("1.0.0", new[] { ("WorkflowA", 1) });

            var supervisor = new RecordingSupervisor("1.0.0");
            var drainWorker = new WorkerDrainWorker(
                supervisor,
                store,
                options: new WorkerDrainOptions { DrainingGracePeriod = TimeSpan.Zero });

            await drainWorker.CheckAndDrainWorkersAsync();
            await drainWorker.CheckAndDrainWorkersAsync();

            supervisor.Events.Should().Equal("archive:1.0.0", "shutdown:1.0.0");
        }

        [Fact]
        public async Task WorkerDrainWorker_ShouldSkipArchivingWhenDisabled()
        {
            using var connection = new SqliteConnection($"Data Source=InMemory_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            connection.Open();
            var options = new DbContextOptionsBuilder<WorkflowsDbContext>().UseSqlite(connection).Options;
            using var context = new WorkflowsDbContext(options);
            context.Database.EnsureCreated();

            var store = new WorkflowStore(context, new TestObjectSerializer());
            await store.RegisterWorkerCapabilitiesAsync("1.0.0", new[] { ("WorkflowA", 1) });

            var supervisor = new RecordingSupervisor("1.0.0");
            var drainWorker = new WorkerDrainWorker(
                supervisor,
                store,
                options: new WorkerDrainOptions
                {
                    DrainingGracePeriod = TimeSpan.Zero,
                    ArchiveBeforeShutdown = false
                });

            await drainWorker.CheckAndDrainWorkersAsync();
            await drainWorker.CheckAndDrainWorkersAsync();

            supervisor.Events.Should().Equal("shutdown:1.0.0");
        }

        private string CreateFakePublish(string version, string executableName)
        {
            var dir = Path.Combine(_root, "publish", version);
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllText(Path.Combine(dir, executableName), "fake-worker");
            File.WriteAllText(Path.Combine(dir, "sub", "runtimeconfig.json"), "{}");
            return dir;
        }

        private sealed class RecordingSupervisor : WorkerProcessSupervisor
        {
            private readonly Dictionary<string, WorkerProcessInfo> _workers;

            public RecordingSupervisor(params string[] versions)
            {
                _workers = versions.ToDictionary(v => v, v => new WorkerProcessInfo { Version = v, PipeName = "pipe-" + v });
            }

            public List<string> Events { get; } = new();

            public override IReadOnlyDictionary<string, WorkerProcessInfo> ActiveWorkers => _workers;

            public override Task<bool> TryArchiveWorkerAsync(string version, string? sourceDirectory = null, CancellationToken ct = default)
            {
                Events.Add("archive:" + version);
                return Task.FromResult(true);
            }

            public override Task ShutdownWorkerAsync(string version, CancellationToken ct = default)
            {
                Events.Add("shutdown:" + version);
                _workers.Remove(version);
                return Task.CompletedTask;
            }
        }
    }
}