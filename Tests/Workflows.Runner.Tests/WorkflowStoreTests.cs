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

        [Fact]
        public async Task FindInstancesWaitingForSignalAsync_RobustJsonAndPathHandling()
        {
            var connection = new SqliteConnection($"Data Source=InMemory_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            connection.Open();
            try
            {
                var options = new DbContextOptionsBuilder<WorkflowsDbContext>()
                    .UseSqlite(connection)
                    .Options;

                using (var context = new TestWorkflowsDbContext(options))
                {
                    context.Database.EnsureCreated();
                }

                var instId1 = Guid.NewGuid();
                var instId2 = Guid.NewGuid();
                var instId3 = Guid.NewGuid();
                var instId4 = Guid.NewGuid();

                using (var context = new TestWorkflowsDbContext(options))
                {
                    context.WorkflowInstances.AddRange(
                        new WorkflowInstance { Id = instId1, Status = (int)WorkflowInstanceStatus.Running, Created = DateTime.UtcNow },
                        new WorkflowInstance { Id = instId2, Status = (int)WorkflowInstanceStatus.Running, Created = DateTime.UtcNow },
                        new WorkflowInstance { Id = instId3, Status = (int)WorkflowInstanceStatus.Running, Created = DateTime.UtcNow },
                        new WorkflowInstance { Id = instId4, Status = (int)WorkflowInstanceStatus.Running, Created = DateTime.UtcNow }
                    );

                    // Wait 1: Exact match with path "$" on primitive JValue
                    context.SignalWaits.Add(new SignalWaitEntity
                    {
                        Id = Guid.NewGuid(),
                        WorkflowInstanceId = instId1,
                        Status = (int)WaitStatus.Waiting,
                        SignalPath = "PrimitiveSignal",
                        SignalExactMatchPaths = "$",
                        ExactMatchFilter = "[\"ORD-123\"]",
                        Created = DateTime.UtcNow
                    });

                    // Wait 2: Match path with invalid JSON path format
                    context.SignalWaits.Add(new SignalWaitEntity
                    {
                        Id = Guid.NewGuid(),
                        WorkflowInstanceId = instId2,
                        Status = (int)WaitStatus.Waiting,
                        SignalPath = "InvalidPathSignal",
                        SignalExactMatchPaths = "invalid..path[",
                        ExactMatchFilter = "[\"\"]",
                        Created = DateTime.UtcNow
                    });

                    // Wait 3 & 4: Distinct Match Paths normalization test (null vs "")
                    context.SignalWaits.Add(new SignalWaitEntity
                    {
                        Id = Guid.NewGuid(),
                        WorkflowInstanceId = instId3,
                        Status = (int)WaitStatus.Waiting,
                        SignalPath = "DuplicateConfigSignal",
                        SignalExactMatchPaths = null!,
                        ExactMatchFilter = string.Empty,
                        Created = DateTime.UtcNow
                    });

                    context.SignalWaits.Add(new SignalWaitEntity
                    {
                        Id = Guid.NewGuid(),
                        WorkflowInstanceId = instId4,
                        Status = (int)WaitStatus.Waiting,
                        SignalPath = "DuplicateConfigSignal",
                        SignalExactMatchPaths = string.Empty,
                        ExactMatchFilter = string.Empty,
                        Created = DateTime.UtcNow
                    });

                    await context.SaveChangesAsync();
                }

                using (var context = new TestWorkflowsDbContext(options))
                {
                    var serializer = new Infrastructure.TestObjectSerializer();
                    var store = new WorkflowStore(context, serializer);

                    // Test 1: Primitive JValue and "$" path
                    var payload1 = "\"ORD-123\"";
                    var matches1 = await store.FindInstancesWaitingForSignalAsync("PrimitiveSignal", payload1);
                    matches1.Should().ContainSingle().Which.Should().Be(instId1);

                    // Test 2: Malformed JSON
                    var payload2 = "{ malformed json";
                    var matches2 = await store.FindInstancesWaitingForSignalAsync("PrimitiveSignal", payload2);
                    matches2.Should().BeEmpty(); // Parsed token is null, so it shouldn't match ORD-123

                    // Test 3: Invalid path format handles gracefully without throwing
                    var payload3 = "{\"key\":\"value\"}";
                    var matches3 = await store.FindInstancesWaitingForSignalAsync("InvalidPathSignal", payload3);
                    // The invalid path "invalid..path[" will throw in SelectToken if not caught.
                    // Our catch block defaults token to null, leading to empty string value matching exact match filter "[\"\"]"
                    matches3.Should().ContainSingle().Which.Should().Be(instId2);

                    // Test 4: Distinct Match Paths normalization (null vs "") avoids duplicate querying and successfully matches both
                    var payload4 = "{}";
                    var matches4 = await store.FindInstancesWaitingForSignalAsync("DuplicateConfigSignal", payload4);
                    matches4.Should().HaveCount(2);
                    matches4.Should().Contain(new[] { instId3, instId4 });
                }
            }
            finally
            {
                connection.Close();
                connection.Dispose();
            }
        }

        private class TestWorkflowsDbContext : WorkflowsDbContext
        {
            public TestWorkflowsDbContext(DbContextOptions<WorkflowsDbContext> options) : base(options)
            {
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                base.OnModelCreating(modelBuilder);
                modelBuilder.Entity<SignalWaitEntity>()
                    .Property(e => e.SignalExactMatchPaths)
                    .IsRequired(false);
            }
        }

        [Fact]
        public async Task StartWorkflow_CalledTwiceWithSameStateAndWaits_ShouldDeduplicateAndReturnSameInstanceId()
        {
            var serializer = new Infrastructure.TestObjectSerializer();
            var instId1 = Guid.NewGuid();
            var instId2 = Guid.NewGuid();

            var instanceData = new TestWorkflowInstance { Value = "unique-test-value" };

            // First run state
            var state1 = new WorkflowStateDto
            {
                Id = instId1,
                WorkflowType = "TestDeduplicationWorkflow",
                Status = WorkflowInstanceStatus.Running,
                Created = DateTime.UtcNow,
                StateObject = new WorkflowStateObject
                {
                    WorkflowType = "TestDeduplicationWorkflow",
                    Instance = instanceData,
                    StateIndex = 0
                },
                Waits = new List<WaitInfrastructureDto>
                {
                    new SignalWaitDto
                    {
                        Id = Guid.NewGuid(),
                        Status = WaitStatus.Waiting,
                        SignalIdentifier = "TestSignal",
                        IsPersisted = false
                    }
                }
            };

            // Second run state (same data, same waits structurally but with new random wait ID and new instance ID)
            var state2 = new WorkflowStateDto
            {
                Id = instId2,
                WorkflowType = "TestDeduplicationWorkflow",
                Status = WorkflowInstanceStatus.Running,
                Created = DateTime.UtcNow,
                StateObject = new WorkflowStateObject
                {
                    WorkflowType = "TestDeduplicationWorkflow",
                    Instance = new TestWorkflowInstance { Value = "unique-test-value" },
                    StateIndex = 0
                },
                Waits = new List<WaitInfrastructureDto>
                {
                    new SignalWaitDto
                    {
                        Id = Guid.NewGuid(),
                        Status = WaitStatus.Waiting,
                        SignalIdentifier = "TestSignal",
                        IsPersisted = false
                    }
                }
            };

            using (var context = new WorkflowsDbContext(_options))
            {
                var store = new WorkflowStore(context, serializer);

                // Save first state
                await store.SaveContextSyncAsync(state1, Enumerable.Empty<Guid>());
                state1.Id.Should().Be(instId1);

                // Save second state (should match and de-duplicate)
                await store.SaveContextSyncAsync(state2, Enumerable.Empty<Guid>());
                state2.Id.Should().Be(instId1); // Should be re-mapped to the first instance ID!
            }

            // Verify only one instance is saved in the database
            using (var context = new WorkflowsDbContext(_options))
            {
                var count = await context.WorkflowInstances.CountAsync(w => w.WorkflowType == "TestDeduplicationWorkflow");
                count.Should().Be(1);
            }
        }

        [Fact]
        public async Task PersistedJson_ShouldHaveZeroTypeProperties()
        {
            // Arrange
            var serializer = new Infrastructure.TestObjectSerializer();
            var instId = Guid.NewGuid();
            var instanceData = new TestWorkflowInstance { Value = "type-free-test-value" };

            var state = new WorkflowStateDto
            {
                Id = instId,
                WorkflowType = "TestTypeFreeWorkflow",
                Status = WorkflowInstanceStatus.Running,
                Created = DateTime.UtcNow,
                StateObject = new WorkflowStateObject
                {
                    WorkflowType = "TestTypeFreeWorkflow",
                    Instance = instanceData,
                    StateIndex = 12
                },
                Waits = new List<WaitInfrastructureDto>
                {
                    new SignalWaitDto
                    {
                        Id = Guid.NewGuid(),
                        Status = WaitStatus.Waiting,
                        SignalIdentifier = "TestSignal",
                        IsPersisted = false
                    }
                }
            };

            using (var context = new WorkflowsDbContext(_options))
            {
                var store = new WorkflowStore(context, serializer);
                await store.SaveContextSyncAsync(state, Enumerable.Empty<Guid>());
            }

            // Act & Assert: Query db directly to verify no "$type" name handling was stored
            using (var context = new WorkflowsDbContext(_options))
            {
                var dbInstance = await context.WorkflowInstances.FirstOrDefaultAsync(wi => wi.Id == instId);
                dbInstance.Should().NotBeNull();

                // Serialize the instance from the database using a simple serializer to inspect the raw DB representation
                var rawStateObjectJson = Newtonsoft.Json.JsonConvert.SerializeObject(dbInstance!.StateObject);
                var rawWaitsJson = Newtonsoft.Json.JsonConvert.SerializeObject(dbInstance.Waits);

                rawStateObjectJson.Should().NotContain("\"$type\"");
                rawWaitsJson.Should().NotContain("\"$type\"");

                // Also make sure we can load/deserialize it back successfully
                var store = new WorkflowStore(context, serializer);
                var loadedState = await store.GetInstanceStateAsync(instId);
                loadedState.Should().NotBeNull();
                loadedState.StateObject.StateIndex.Should().Be(12);
            }
        }

        private class TestWorkflowInstance
        {
            public string Value { get; set; }
        }
    }
}
