using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Persistence;
using Workflows.Communication.Abstraction;
using Workflows.Definition;
using Workflows.Runner;
using Workflows.Runner.Migration;
using Workflows.Storage.EntityFrameworkCore;
using Xunit;

using Workflows.Runner.Tests.Infrastructure;

namespace Workflows.Storage.Tests
{
    /// <summary>
    /// Migration whose phase 1 always throws, so the executor's failure path can be observed.
    /// </summary>
    [WorkflowMigration("FailingOrderWorkflow", fromVersion: 1, toVersion: 2)]
    public class FailingOrderWorkflowMigration_V1_To_V2
        : WorkflowMigration<AdvancedOrderWorkflowV1Wrapper, AdvancedOrderWorkflowV2Wrapper>
    {
        public override void MigrateState(AdvancedOrderWorkflowV1Wrapper old, AdvancedOrderWorkflowV2Wrapper _new)
        {
            throw new InvalidOperationException("state mapping exploded");
        }

        public override MigratedWait MigrateActiveWait(WaitInfrastructureDto oldWait, AdvancedOrderWorkflowV2Wrapper _new)
        {
            return RecreateWait(oldWait.WaitName);
        }
    }

    internal sealed class NoOpMessageDispatcher : IMessageDispatcher
    {
        public Task DispatchAsync<T>(T message) => Task.CompletedTask;

        public Task<TResponse> DispatchAndReceiveAsync<TRequest, TResponse>(TRequest message)
            => Task.FromResult<TResponse>(default!);
    }

    public class MigrationSnapshotTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<WorkflowsDbContext> _options;

        public MigrationSnapshotTests()
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
        public async Task SaveInstanceSnapshotAsync_ShouldRoundTripOldestFirst()
        {
            using var context = new WorkflowsDbContext(_options);
            var store = new WorkflowStore(context, new TestObjectSerializer());
            var instanceId = Guid.NewGuid();

            await store.SaveInstanceSnapshotAsync(new WorkflowInstanceSnapshot
            {
                InstanceId = instanceId,
                WorkflowType = "SnapshotWorkflow",
                FromVersion = 1,
                ToVersion = 2,
                Reason = "first",
                StateJson = "{\"a\":1}",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

            await store.SaveInstanceSnapshotAsync(new WorkflowInstanceSnapshot
            {
                InstanceId = instanceId,
                WorkflowType = "SnapshotWorkflow",
                FromVersion = 1,
                ToVersion = 2,
                Reason = "second",
                StateJson = "{\"a\":2}",
                CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            });

            var snapshots = await store.GetInstanceSnapshotsAsync(instanceId);

            snapshots.Should().HaveCount(2);
            snapshots.Select(s => s.Reason).Should().ContainInOrder("first", "second");
            snapshots[0].StateJson.Should().Be("{\"a\":1}");
        }

        [Fact]
        public async Task MigrateAsync_ShouldSnapshotAndParkInstanceWhenMigrationThrows()
        {
            var services = new ServiceCollection();
            services.AddDbContext<WorkflowsDbContext>(opt => opt.UseSqlite(_connection));
            services.AddScoped<IWorkflowStore, WorkflowStore>();
            services.AddSingleton<IObjectSerializer, TestObjectSerializer>();
            services.AddSingleton<IExpressionSerializer, TestExpressionSerializer>();
            services.AddWorkflowsRunner();
            services.AddSingleton<IMessageDispatcher>(new NoOpMessageDispatcher());
            services.AddWorkflowMigration<AdvancedOrderWorkflowV1Wrapper, AdvancedOrderWorkflowV2Wrapper, FailingOrderWorkflowMigration_V1_To_V2>();

            var sp = services.BuildServiceProvider();

            var instanceId = Guid.NewGuid();
            var v1State = new WorkflowStateDto
            {
                Id = instanceId,
                Created = DateTime.UtcNow,
                Status = WorkflowInstanceStatus.Running,
                WorkflowType = "FailingOrderWorkflow",
                WorkflowVersion = 1,
                StateObject = new WorkflowStateObject
                {
                    WorkflowType = "FailingOrderWorkflow",
                    Instance = new object(),
                    StateIndex = 1,
                    Locals = new Dictionary<string, object>
                    {
                        { "state", new OrderWorkflowAdvancedState_V1 { OrderId = instanceId, CustomerId = "CUST-1", Amount = 10m } }
                    }
                },
                Waits = new List<WaitInfrastructureDto>
                {
                    new SignalWaitDto
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = WaitStatus.Waiting,
                        WaitName = "ApprovalWait",
                        SignalIdentifier = "ApprovalSignal",
                        IsPersisted = false
                    }
                }
            };

            var store = sp.GetRequiredService<IWorkflowStore>();
            await store.SaveContextSyncAsync(v1State, Enumerable.Empty<string>());

            var executor = sp.GetKeyedService<IWorkflowMigrationExecutor>("FailingOrderWorkflow:1:2");
            executor.Should().NotBeNull();

            var act = () => executor!.MigrateAsync(instanceId, CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>();

            using var verifyContext = new WorkflowsDbContext(_options);
            var instance = await verifyContext.WorkflowInstances.SingleAsync(i => i.Id == instanceId);

            instance.WorkflowVersion.Should().Be(1);
            instance.Status.Should().Be((int)WorkflowInstanceStatus.MigrationFailed);

            var snapshots = await store.GetInstanceSnapshotsAsync(instanceId);
            snapshots.Should().ContainSingle();
            snapshots[0].FromVersion.Should().Be(1);
            snapshots[0].ToVersion.Should().Be(2);
            snapshots[0].Reason.Should().Be(nameof(FailingOrderWorkflowMigration_V1_To_V2));
            snapshots[0].StateJson.Should().Contain("CUST-1");
        }
    }
}