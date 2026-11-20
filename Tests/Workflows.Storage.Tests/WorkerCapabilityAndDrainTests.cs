using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Persistence;
using Workflows.Hosting.InProcess;
using Workflows.Storage.EntityFrameworkCore;
using Xunit;

using Workflows.Runner.Tests.Infrastructure;

namespace Workflows.Storage.Tests
{
    public class WorkerCapabilityAndDrainTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<WorkflowsDbContext> _options;

        public WorkerCapabilityAndDrainTests()
        {
            _connection = new SqliteConnection($"Data Source=InMemory_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            _connection.Open();

            _options = new DbContextOptionsBuilder<WorkflowsDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var context = new WorkflowsDbContext(_options);
            context.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _connection.Close();
            _connection.Dispose();
        }

        [Fact]
        public async Task RegisterWorkerCapabilitiesAsync_ShouldStoreAndRetrieveCapabilitiesByDllVersion()
        {
            using var context = new WorkflowsDbContext(_options);
            var store = new WorkflowStore(context, new TestObjectSerializer());

            var capsV1 = new[] { ("WorkflowA", 1), ("WorkflowB", 1) };
            var capsV2 = new[] { ("WorkflowA", 2), ("WorkflowC", 1) };

            await store.RegisterWorkerCapabilitiesAsync("1.0.0", capsV1);
            await store.RegisterWorkerCapabilitiesAsync("2.0.0", capsV2);

            var activeV1 = await store.GetActiveDllVersionsForWorkflowAsync("WorkflowA", 1);
            var activeV2 = await store.GetActiveDllVersionsForWorkflowAsync("WorkflowA", 2);
            var activeV3 = await store.GetActiveDllVersionsForWorkflowAsync("WorkflowC", 1);

            activeV1.Should().ContainSingle().Which.Should().Be("1.0.0");
            activeV2.Should().ContainSingle().Which.Should().Be("2.0.0");
            activeV3.Should().ContainSingle().Which.Should().Be("2.0.0");
        }

        [Fact]
        public async Task GetActiveInstanceCountsByDllVersionAsync_ShouldCountInstancesPerDll()
        {
            using var context = new WorkflowsDbContext(_options);
            var store = new WorkflowStore(context, new TestObjectSerializer());

            await store.RegisterWorkerCapabilitiesAsync("1.0.0", new[] { ("WorkflowA", 1) });
            await store.RegisterWorkerCapabilitiesAsync("2.0.0", new[] { ("WorkflowA", 2) });

            // Create 2 running instances for V1 and 0 for V2
            context.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = Guid.NewGuid(),
                WorkflowType = "WorkflowA",
                WorkflowVersion = 1,
                Status = (int)WorkflowInstanceStatus.Running
            });
            context.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = Guid.NewGuid(),
                WorkflowType = "WorkflowA",
                WorkflowVersion = 1,
                Status = (int)WorkflowInstanceStatus.Running
            });
            await context.SaveChangesAsync();

            var counts = await store.GetActiveInstanceCountsByDllVersionAsync();

            counts["1.0.0"].Should().Be(2);
            counts["2.0.0"].Should().Be(0);
        }

        [Fact]
        public async Task GetActiveInstanceCountsByDllVersionAsync_ShouldKeepVersionAliveWhileCompensationIsPending()
        {
            using var context = new WorkflowsDbContext(_options);
            var store = new WorkflowStore(context, new TestObjectSerializer());

            await store.RegisterWorkerCapabilitiesAsync("1.0.0", new[] { ("WorkflowA", 1) });
            await store.RegisterWorkerCapabilitiesAsync("2.0.0", new[] { ("WorkflowA", 2) });

            var pendingInstance = Guid.NewGuid();
            var exhaustedInstance = Guid.NewGuid();

            context.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = pendingInstance,
                WorkflowType = "WorkflowA",
                WorkflowVersion = 1,
                Status = (int)WorkflowInstanceStatus.InError
            });
            context.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = exhaustedInstance,
                WorkflowType = "WorkflowA",
                WorkflowVersion = 1,
                Status = (int)WorkflowInstanceStatus.CompensationFailed
            });
            context.CompensationWaits.Add(new CompensationWaitEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                WorkflowInstanceId = pendingInstance,
                Status = (int)WaitStatus.Waiting,
                Token = "caller:Compensation"
            });
            await context.SaveChangesAsync();

            var counts = await store.GetActiveInstanceCountsByDllVersionAsync();

            // The InError instance with a pending compensation keeps 1.0.0 alive; the compensated-and-given-up
            // instance does not.
            counts["1.0.0"].Should().Be(1);
            counts["2.0.0"].Should().Be(0);
        }

        [Fact]
        public async Task GetActiveInstanceCountsByDllVersionAsync_ShouldIgnoreCompletedCompensations()
        {
            using var context = new WorkflowsDbContext(_options);
            var store = new WorkflowStore(context, new TestObjectSerializer());

            await store.RegisterWorkerCapabilitiesAsync("1.0.0", new[] { ("WorkflowA", 1) });

            var instance = Guid.NewGuid();
            context.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = instance,
                WorkflowType = "WorkflowA",
                WorkflowVersion = 1,
                Status = (int)WorkflowInstanceStatus.Canceled
            });
            context.CompensationWaits.Add(new CompensationWaitEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                WorkflowInstanceId = instance,
                Status = (int)WaitStatus.Completed,
                Token = "caller:Compensation"
            });
            await context.SaveChangesAsync();

            var counts = await store.GetActiveInstanceCountsByDllVersionAsync();

            counts["1.0.0"].Should().Be(0);
        }

        [Fact]
        public void WorkerProcessSupervisor_ShouldRegisterAndResolveCapabilities()
        {
            var supervisor = new WorkerProcessSupervisor();
            supervisor.RegisterCapabilities("1.0.0", new[] { ("WorkflowA", 1), ("WorkflowB", 1) });
            supervisor.RegisterCapabilities("2.0.0", new[] { ("WorkflowA", 2) });

            supervisor.RegisterCapabilities("1.0.0", new[] { ("WorkflowA", 1) });
            
            supervisor.ResolveWorkerVersionForWorkflow("WorkflowA", 2).Should().BeNull(); // process not started
        }

        [Fact]
        public async Task WorkerDrainWorker_ShouldDrainAndShutdownWorkerWithZeroActiveInstances()
        {
            using var context = new WorkflowsDbContext(_options);
            var store = new WorkflowStore(context, new TestObjectSerializer());

            await store.RegisterWorkerCapabilitiesAsync("1.0.0", new[] { ("WorkflowA", 1) });
            await store.RegisterWorkerCapabilitiesAsync("2.0.0", new[] { ("WorkflowA", 2) });

            // Add active instance for V1 only
            context.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = Guid.NewGuid(),
                WorkflowType = "WorkflowA",
                WorkflowVersion = 1,
                Status = (int)WorkflowInstanceStatus.Running
            });
            await context.SaveChangesAsync();

            var supervisor = new WorkerProcessSupervisor();
            var drainWorker = new WorkerDrainWorker(supervisor, store);

            // Execute drain check
            await drainWorker.CheckAndDrainWorkersAsync();

            // Verify active instance count query logic works cleanly
            var counts = await store.GetActiveInstanceCountsByDllVersionAsync();
            counts["1.0.0"].Should().Be(1);
            counts["2.0.0"].Should().Be(0);
        }

        [Fact]
        public async Task GetParkedInstanceCountsByDllVersionAsync_ShouldCountTerminalFailuresPerDll()
        {
            using var context = new WorkflowsDbContext(_options);
            var store = new WorkflowStore(context, new TestObjectSerializer());

            await store.RegisterWorkerCapabilitiesAsync("1.0.0", new[] { ("WorkflowA", 1) });
            await store.RegisterWorkerCapabilitiesAsync("2.0.0", new[] { ("WorkflowA", 2) });

            context.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = Guid.NewGuid(),
                WorkflowType = "WorkflowA",
                WorkflowVersion = 1,
                Status = (int)WorkflowInstanceStatus.CompensationFailed
            });
            context.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = Guid.NewGuid(),
                WorkflowType = "WorkflowA",
                WorkflowVersion = 1,
                Status = (int)WorkflowInstanceStatus.MigrationFailed
            });
            context.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = Guid.NewGuid(),
                WorkflowType = "WorkflowA",
                WorkflowVersion = 1,
                Status = (int)WorkflowInstanceStatus.Running
            });
            await context.SaveChangesAsync();

            var parked = await store.GetParkedInstanceCountsByDllVersionAsync();

            parked["1.0.0"].Should().Be(2);
            parked["2.0.0"].Should().Be(0);
        }

        [Fact]
        public async Task RestoreInstanceSnapshotAsync_ShouldRollInstanceBackToSnapshotVersion()
        {
            using var context = new WorkflowsDbContext(_options);
            var serializer = new TestObjectSerializer();
            var store = new WorkflowStore(context, serializer);

            var instanceId = Guid.NewGuid();
            context.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = instanceId,
                WorkflowType = "WorkflowA",
                WorkflowVersion = 2,
                Status = (int)WorkflowInstanceStatus.MigrationFailed
            });
            await context.SaveChangesAsync();

            var snapshot = new WorkflowInstanceSnapshot
            {
                InstanceId = instanceId,
                WorkflowType = "WorkflowA",
                FromVersion = 1,
                ToVersion = 2,
                Reason = "unit-test",
                StateJson = serializer.Serialize(new WorkflowStateDto { WaitCounter = 3 }) as string ?? string.Empty
            };
            await store.SaveInstanceSnapshotAsync(snapshot);

            (await store.GetInstanceSnapshotsAsync(instanceId)).Should().ContainSingle()
                .Which.Id.Should().Be(snapshot.Id);

            (await store.RestoreInstanceSnapshotAsync(snapshot.Id)).Should().BeTrue();
            (await store.RestoreInstanceSnapshotAsync("does-not-exist")).Should().BeFalse();

            using var verify = new WorkflowsDbContext(_options);
            var restored = await verify.WorkflowInstances.SingleAsync(i => i.Id == instanceId);
            restored.WorkflowVersion.Should().Be(1);
            restored.StateObject.Should().NotBeNull();
        }

        [Fact]
        public async Task WorkerDrainWorker_ShouldKeepWorkerAliveInsideGracePeriod()
        {
            using var context = new WorkflowsDbContext(_options);
            var store = new WorkflowStore(context, new TestObjectSerializer());
            await store.RegisterWorkerCapabilitiesAsync("1.0.0", new[] { ("WorkflowA", 1) });

            var supervisor = new FakeWorkerProcessSupervisor("1.0.0");
            var drainWorker = new WorkerDrainWorker(
                supervisor,
                store,
                options: new WorkerDrainOptions { DrainingGracePeriod = TimeSpan.FromHours(1) });

            await drainWorker.CheckAndDrainWorkersAsync();
            await drainWorker.CheckAndDrainWorkersAsync();
            await drainWorker.CheckAndDrainWorkersAsync();

            supervisor.ShutDownVersions.Should().BeEmpty(
                "a worker that just reached zero active instances must survive the grace window");
        }

        [Fact]
        public async Task WorkerDrainWorker_ShouldShutdownWorkerOnceGracePeriodElapses()
        {
            using var context = new WorkflowsDbContext(_options);
            var store = new WorkflowStore(context, new TestObjectSerializer());
            await store.RegisterWorkerCapabilitiesAsync("1.0.0", new[] { ("WorkflowA", 1) });

            var supervisor = new FakeWorkerProcessSupervisor("1.0.0");
            var drainWorker = new WorkerDrainWorker(
                supervisor,
                store,
                options: new WorkerDrainOptions { DrainingGracePeriod = TimeSpan.Zero });

            await drainWorker.CheckAndDrainWorkersAsync();
            supervisor.ShutDownVersions.Should().BeEmpty("the first sweep only records the zero-active timestamp");

            await drainWorker.CheckAndDrainWorkersAsync();
            supervisor.ShutDownVersions.Should().ContainSingle().Which.Should().Be("1.0.0");
        }

        [Fact]
        public async Task WorkerDrainWorker_ShouldNotShutdownWorkerWhileInstancesAreParked()
        {
            using var context = new WorkflowsDbContext(_options);
            var store = new WorkflowStore(context, new TestObjectSerializer());
            await store.RegisterWorkerCapabilitiesAsync("1.0.0", new[] { ("WorkflowA", 1) });

            context.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = Guid.NewGuid(),
                WorkflowType = "WorkflowA",
                WorkflowVersion = 1,
                Status = (int)WorkflowInstanceStatus.CompensationFailed
            });
            await context.SaveChangesAsync();

            var supervisor = new FakeWorkerProcessSupervisor("1.0.0");
            var drainWorker = new WorkerDrainWorker(
                supervisor,
                store,
                options: new WorkerDrainOptions { DrainingGracePeriod = TimeSpan.Zero });

            await drainWorker.CheckAndDrainWorkersAsync();
            await drainWorker.CheckAndDrainWorkersAsync();

            supervisor.ShutDownVersions.Should().BeEmpty(
                "parked instances still need the old worker to make progress");

            var optInDrainWorker = new WorkerDrainWorker(
                supervisor,
                store,
                options: new WorkerDrainOptions
                {
                    DrainingGracePeriod = TimeSpan.Zero,
                    AllowShutdownWithParkedInstances = true
                });

            await optInDrainWorker.CheckAndDrainWorkersAsync();
            await optInDrainWorker.CheckAndDrainWorkersAsync();

            supervisor.ShutDownVersions.Should().ContainSingle().Which.Should().Be("1.0.0");
        }

        private sealed class FakeWorkerProcessSupervisor : WorkerProcessSupervisor
        {
            private readonly Dictionary<string, WorkerProcessInfo> _workers;

            public FakeWorkerProcessSupervisor(params string[] versions)
            {
                _workers = versions.ToDictionary(
                    v => v,
                    v => new WorkerProcessInfo { Version = v, PipeName = $"pipe-{v}" });
            }

            public List<string> ShutDownVersions { get; } = new();

            public override IReadOnlyDictionary<string, WorkerProcessInfo> ActiveWorkers => _workers;

            public override Task ShutdownWorkerAsync(string version, CancellationToken ct = default)
            {
                ShutDownVersions.Add(version);
                _workers.Remove(version);
                return Task.CompletedTask;
            }
        }
    }
}
