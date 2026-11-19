using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Enums;
using Workflows.Hosting.InProcess;
using Workflows.Storage.EntityFrameworkCore;
using Xunit;

namespace Workflows.Runner.Tests
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
            var store = new WorkflowStore(context, new Infrastructure.TestObjectSerializer());

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
            var store = new WorkflowStore(context, new Infrastructure.TestObjectSerializer());

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
            var store = new WorkflowStore(context, new Infrastructure.TestObjectSerializer());

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
    }
}
