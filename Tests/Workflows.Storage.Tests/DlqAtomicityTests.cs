using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Persistence;
using Workflows.Runner.Tests.Infrastructure;
using Workflows.Storage.EntityFrameworkCore;
using Xunit;

namespace Workflows.Storage.Tests
{
    /// <summary>
    /// Proves the dead letter row and the terminal status change are committed by the same transaction:
    /// a stuck rollback is never persisted without its DLQ entry, and a failed save leaves neither behind.
    /// </summary>
    public class DlqAtomicityTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<WorkflowsDbContext> _options;
        private readonly TestObjectSerializer _serializer = new();

        public DlqAtomicityTests()
        {
            _connection = new SqliteConnection($"Data Source=DlqAtomicity_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
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

        private static WorkflowStateDto CreateState(Guid id, string workflowType, WorkflowInstanceStatus status)
        {
            return new WorkflowStateDto
            {
                Id = id,
                WorkflowType = workflowType,
                WorkflowVersion = 1,
                Status = status,
                Created = DateTime.UtcNow,
                Waits = new List<WaitInfrastructureDto>()
            };
        }

        private static DlqMessage CreateDlqMessage(Guid instanceId, string workflowType)
        {
            return new DlqMessage
            {
                InstanceId = instanceId,
                WorkflowType = workflowType,
                FailureCount = 2,
                PayloadJson = "{}",
                ErrorMessage = "compensation failed",
                FailedAt = DateTime.UtcNow
            };
        }

        [Fact]
        public async Task SaveContextSyncAsync_WithDlqMessage_ShouldPersistStatusAndDlqRowTogether()
        {
            var instanceId = Guid.NewGuid();
            const string workflowType = "DlqAtomicWorkflow";

            using (var context = new WorkflowsDbContext(_options))
            {
                var store = new WorkflowStore(context, _serializer);
                await store.SaveContextSyncAsync(
                    CreateState(instanceId, workflowType, WorkflowInstanceStatus.InError),
                    Enumerable.Empty<string>());
            }

            using (var context = new WorkflowsDbContext(_options))
            {
                var store = new WorkflowStore(context, _serializer);
                await store.SaveContextSyncAsync(
                    CreateState(instanceId, workflowType, WorkflowInstanceStatus.CompensationFailed),
                    Enumerable.Empty<string>(),
                    null,
                    CreateDlqMessage(instanceId, workflowType));
            }

            using (var context = new WorkflowsDbContext(_options))
            {
                var instance = await context.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == instanceId);
                instance.Status.Should().Be((int)WorkflowInstanceStatus.CompensationFailed);

                var dlqRows = await context.DlqMessages.AsNoTracking()
                    .Where(d => d.WorkflowInstanceId == instanceId)
                    .ToListAsync();

                dlqRows.Should().ContainSingle();
                dlqRows[0].WorkflowType.Should().Be(workflowType);
                dlqRows[0].FailureCount.Should().Be(2);
                dlqRows[0].ErrorMessage.Should().Be("compensation failed");
            }
        }

        [Fact]
        public async Task SaveContextSyncAsync_WhenSaveFails_ShouldRollBackTheDlqRowToo()
        {
            var instanceId = Guid.NewGuid();
            const string workflowType = "ConcurrencyRetryTest";

            using (var context = new WorkflowsDbContext(_options))
            {
                var store = new WorkflowStore(context, _serializer);
                await store.SaveContextSyncAsync(
                    CreateState(instanceId, workflowType, WorkflowInstanceStatus.InError),
                    Enumerable.Empty<string>());
            }

            try
            {
                // Forces the store to throw between the entity graph update and SaveChanges.
                WorkflowStore.MockConcurrencyRetryCount = 1;

                using var context = new WorkflowsDbContext(_options);
                var store = new WorkflowStore(context, _serializer);

                Func<Task> act = () => store.SaveContextSyncAsync(
                    CreateState(instanceId, workflowType, WorkflowInstanceStatus.CompensationFailed),
                    Enumerable.Empty<string>(),
                    null,
                    CreateDlqMessage(instanceId, workflowType));

                await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
            }
            finally
            {
                WorkflowStore.MockConcurrencyRetryCount = 0;
            }

            using (var verify = new WorkflowsDbContext(_options))
            {
                (await verify.DlqMessages.AsNoTracking().CountAsync(d => d.WorkflowInstanceId == instanceId))
                    .Should().Be(0);

                var instance = await verify.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == instanceId);
                instance.Status.Should().Be((int)WorkflowInstanceStatus.InError);
            }
        }

        [Fact]
        public async Task AppendDlqMessageAsync_ShouldRemainUsableStandalone()
        {
            var instanceId = Guid.NewGuid();
            const string workflowType = "DlqStandaloneWorkflow";

            using var context = new WorkflowsDbContext(_options);
            var store = new WorkflowStore(context, _serializer);
            await store.AppendDlqMessageAsync(CreateDlqMessage(instanceId, workflowType));

            using var verify = new WorkflowsDbContext(_options);
            (await verify.DlqMessages.AsNoTracking().CountAsync(d => d.WorkflowInstanceId == instanceId))
                .Should().Be(1);
        }
    }
}