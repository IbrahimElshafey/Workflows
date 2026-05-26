using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Storage.EntityFrameworkCore;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class WorkflowStoreTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<WorkflowsDbContext> _options;

        public WorkflowStoreTests()
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
        public async Task FindInstancesWaitingForSignalAsync_ShouldCorrectlyMatchExactFilters()
        {
            var instId1 = Guid.NewGuid();
            var instId2 = Guid.NewGuid();
            var instId3 = Guid.NewGuid();
            var instId4 = Guid.NewGuid();

            using (var context = new WorkflowsDbContext(_options))
            {
                // Set up WorkflowInstances
                context.WorkflowInstances.AddRange(
                    new WorkflowInstance { Id = instId1, Status = (int)WorkflowInstanceStatus.Running, Created = DateTime.UtcNow },
                    new WorkflowInstance { Id = instId2, Status = (int)WorkflowInstanceStatus.Running, Created = DateTime.UtcNow },
                    new WorkflowInstance { Id = instId3, Status = (int)WorkflowInstanceStatus.Running, Created = DateTime.UtcNow },
                    new WorkflowInstance { Id = instId4, Status = (int)WorkflowInstanceStatus.Running, Created = DateTime.UtcNow }
                );

                // Set up SignalWaits
                // Wait 1: Exact match on OrderId (ORD-123)
                context.SignalWaits.Add(new SignalWaitEntity
                {
                    Id = Guid.NewGuid(),
                    WorkflowInstanceId = instId1,
                    Status = (int)WaitStatus.Waiting,
                    SignalPath = "OrderSignal",
                    SignalExactMatchPaths = "OrderId",
                    ExactMatchFilter = "[\"ORD-123\"]",
                    Created = DateTime.UtcNow
                });

                // Wait 2: Exact match on OrderId (ORD-456)
                context.SignalWaits.Add(new SignalWaitEntity
                {
                    Id = Guid.NewGuid(),
                    WorkflowInstanceId = instId2,
                    Status = (int)WaitStatus.Waiting,
                    SignalPath = "OrderSignal",
                    SignalExactMatchPaths = "OrderId",
                    ExactMatchFilter = "[\"ORD-456\"]",
                    Created = DateTime.UtcNow
                });

                // Wait 3: Exact match on OrderId and Amount (ORD-123, 250)
                context.SignalWaits.Add(new SignalWaitEntity
                {
                    Id = Guid.NewGuid(),
                    WorkflowInstanceId = instId3,
                    Status = (int)WaitStatus.Waiting,
                    SignalPath = "OrderSignal",
                    SignalExactMatchPaths = "OrderId,Amount",
                    ExactMatchFilter = "[\"ORD-123\",\"250\"]",
                    Created = DateTime.UtcNow
                });

                // Wait 4: Broadcast Wait (no exact match configuration)
                context.SignalWaits.Add(new SignalWaitEntity
                {
                    Id = Guid.NewGuid(),
                    WorkflowInstanceId = instId4,
                    Status = (int)WaitStatus.Waiting,
                    SignalPath = "OrderSignal",
                    SignalExactMatchPaths = string.Empty,
                    ExactMatchFilter = string.Empty,
                    Created = DateTime.UtcNow
                });

                await context.SaveChangesAsync();
            }

            using (var context = new WorkflowsDbContext(_options))
            {
                var serializer = new Infrastructure.TestObjectSerializer();
                var store = new WorkflowStore(context, serializer);

                // Scenario A: Signal matches Wait 1 (OrderId="ORD-123") and Broadcast Wait (Wait 4)
                var payloadA = "{\"OrderId\":\"ORD-123\",\"Amount\":999}";
                var matchesA = await store.FindInstancesWaitingForSignalAsync("OrderSignal", payloadA);
                matchesA.Should().Contain(instId1);
                matchesA.Should().Contain(instId4);
                matchesA.Should().NotContain(instId2);
                matchesA.Should().NotContain(instId3);

                // Scenario B: Signal matches Wait 2 (OrderId="ORD-456") and Broadcast Wait (Wait 4)
                var payloadB = "{\"OrderId\":\"ORD-456\",\"Amount\":999}";
                var matchesB = await store.FindInstancesWaitingForSignalAsync("OrderSignal", payloadB);
                matchesB.Should().Contain(instId2);
                matchesB.Should().Contain(instId4);
                matchesB.Should().NotContain(instId1);
                matchesB.Should().NotContain(instId3);

                // Scenario C: Signal matches Wait 3 (OrderId="ORD-123", Amount=250), Wait 1 (OrderId="ORD-123"), and Broadcast Wait (Wait 4)
                var payloadC = "{\"OrderId\":\"ORD-123\",\"Amount\":250}";
                var matchesC = await store.FindInstancesWaitingForSignalAsync("OrderSignal", payloadC);
                matchesC.Should().Contain(instId1);
                matchesC.Should().Contain(instId3);
                matchesC.Should().Contain(instId4);
                matchesC.Should().NotContain(instId2);

                // Scenario D: Signal payload is empty, should return all waiting instances
                var matchesD = await store.FindInstancesWaitingForSignalAsync("OrderSignal", null);
                matchesD.Should().Contain(new[] { instId1, instId2, instId3, instId4 });
            }
        }

        [Fact]
        public async Task SaveAndLoadTemplateHashKey_ShouldCorrectlyPersist()
        {
            var instId = Guid.NewGuid();
            var waitId = Guid.NewGuid();

            using (var context = new WorkflowsDbContext(_options))
            {
                var serializer = new Infrastructure.TestObjectSerializer();
                var store = new WorkflowStore(context, serializer);

                var state = new WorkflowStateDto
                {
                    Id = instId,
                    WorkflowType = "TestWorkflow",
                    Status = WorkflowInstanceStatus.Running,
                    Created = DateTime.UtcNow,
                    Waits = new List<WaitInfrastructureDto>
                    {
                        new SignalWaitDto
                        {
                            Id = waitId,
                            Status = WaitStatus.Waiting,
                            SignalIdentifier = "TestSignal",
                            TemplateHashKey = "HashKey-12345",
                            IsPersisted = false
                        }
                    }
                };

                await store.SaveContextSyncAsync(state, Enumerable.Empty<Guid>());
            }

            using (var context = new WorkflowsDbContext(_options))
            {
                var signalWait = await context.SignalWaits.FindAsync(waitId);
                signalWait.Should().NotBeNull();
                signalWait!.TemplateHashKey.Should().Be("HashKey-12345");
            }
        }
    }
}
