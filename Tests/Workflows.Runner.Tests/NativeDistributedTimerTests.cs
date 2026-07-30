using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Persistence;
using Workflows.Orchestrator;
using Workflows.Runner.Tests.Infrastructure;
using Workflows.Storage.EntityFrameworkCore;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class NativeDistributedTimerTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<WorkflowsDbContext> _options;

        public NativeDistributedTimerTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<WorkflowsDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var context = new WorkflowsDbContext(_options);
            context.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _connection.Dispose();
        }

        [Fact]
        public async Task ClaimDueTimersAsync_ShouldClaimWaitingTimersAtomically()
        {
            // Arrange
            using var context = new WorkflowsDbContext(_options);
            var serializer = new TestObjectSerializer();
            var store = new WorkflowStore(context, serializer);

            var instanceId = Guid.NewGuid();
            context.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = instanceId,
                WorkflowType = "TestWorkflow",
                WorkflowVersion = 1,
                Status = (int)WorkflowInstanceStatus.Running,
                Created = DateTime.UtcNow
            });

            var timer1Id = Guid.NewGuid().ToString();
            var timer2Id = Guid.NewGuid().ToString();

            context.TimeWaits.Add(new TimeWaitEntity
            {
                Id = timer1Id,
                WorkflowInstanceId = instanceId,
                UniqueMatchId = "Timer_1",
                ExecutionTime = DateTime.UtcNow.AddSeconds(-10), // Due
                Status = (int)WaitStatus.Waiting
            });

            context.TimeWaits.Add(new TimeWaitEntity
            {
                Id = timer2Id,
                WorkflowInstanceId = instanceId,
                UniqueMatchId = "Timer_2",
                ExecutionTime = DateTime.UtcNow.AddMinutes(10), // Future (Not Due)
                Status = (int)WaitStatus.Waiting
            });

            await context.SaveChangesAsync();

            // Act - Node A claims due timers
            var claimedNodeA = await store.ClaimDueTimersAsync(10, "Node_A");

            // Assert
            claimedNodeA.Should().HaveCount(1);
            claimedNodeA[0].Id.Should().Be(timer1Id);

            // Act - Node B attempts to claim due timers concurrently
            var claimedNodeB = await store.ClaimDueTimersAsync(10, "Node_B");

            // Assert - Node B receives 0 timers because Node A already claimed timer1
            claimedNodeB.Should().BeEmpty();
        }

        [Fact]
        public async Task RecoverStaleTimersAsync_ShouldResetStaleMatchedTimersToWaiting()
        {
            // Arrange
            using var context = new WorkflowsDbContext(_options);
            var serializer = new TestObjectSerializer();
            var store = new WorkflowStore(context, serializer);

            var instanceId = Guid.NewGuid();
            context.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = instanceId,
                WorkflowType = "StaleWorkflow",
                WorkflowVersion = 1,
                Status = (int)WorkflowInstanceStatus.Running,
                Created = DateTime.UtcNow
            });

            var timerId = Guid.NewGuid().ToString();
            context.TimeWaits.Add(new TimeWaitEntity
            {
                Id = timerId,
                WorkflowInstanceId = instanceId,
                UniqueMatchId = "StaleTimer",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-10), // Matched 10 mins ago
                Status = (int)WaitStatus.Matched
            });

            await context.SaveChangesAsync();

            // Act - Recover stale timers older than 5 minutes
            await store.RecoverStaleTimersAsync(TimeSpan.FromMinutes(5));

            // Assert
            var recoveredTimer = await context.TimeWaits.FindAsync(timerId);
            recoveredTimer.Should().NotBeNull();
            recoveredTimer!.Status.Should().Be((int)WaitStatus.Waiting);
        }

        [Fact]
        public async Task ClusteredScheduler_MultiNodeConcurrentExecution_ZeroDuplicates()
        {
            // Arrange
            using var setupContext = new WorkflowsDbContext(_options);
            var instanceId = Guid.NewGuid();
            setupContext.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = instanceId,
                WorkflowType = "MultiNodeWorkflow",
                WorkflowVersion = 1,
                Status = (int)WorkflowInstanceStatus.Running,
                Created = DateTime.UtcNow
            });

            int totalTimers = 20;
            var expectedTimerIds = new HashSet<string>();
            for (int i = 0; i < totalTimers; i++)
            {
                var id = Guid.NewGuid().ToString();
                expectedTimerIds.Add(id);
                setupContext.TimeWaits.Add(new TimeWaitEntity
                {
                    Id = id,
                    WorkflowInstanceId = instanceId,
                    UniqueMatchId = $"Signal_{i}",
                    ExecutionTime = DateTime.UtcNow.AddMilliseconds(-50), // All due immediately
                    Status = (int)WaitStatus.Waiting
                });
            }
            await setupContext.SaveChangesAsync();

            var processedSignals = new List<string>();
            var lockObj = new object();

            var mockOrchestrator = new TestMockOrchestrator(signal =>
            {
                lock (lockObj)
                {
                    processedSignals.Add(signal.Id.ToString());
                }
                return Task.CompletedTask;
            });

            // Service providers for Node A and Node B
            var servicesA = new ServiceCollection();
            servicesA.AddDbContext<WorkflowsDbContext>(o => o.UseSqlite(_connection));
            servicesA.AddSingleton<IObjectSerializer, TestObjectSerializer>();
            servicesA.AddScoped<IWorkflowStore, WorkflowStore>();
            servicesA.AddSingleton<IOrchestrator>(mockOrchestrator);
            var spA = servicesA.BuildServiceProvider();

            var servicesB = new ServiceCollection();
            servicesB.AddDbContext<WorkflowsDbContext>(o => o.UseSqlite(_connection));
            servicesB.AddSingleton<IObjectSerializer, TestObjectSerializer>();
            servicesB.AddScoped<IWorkflowStore, WorkflowStore>();
            servicesB.AddSingleton<IOrchestrator>(mockOrchestrator);
            var spB = servicesB.BuildServiceProvider();

            var schedulerA = new Scheduler(spA);
            var schedulerB = new Scheduler(spB);

            // Act - Start both scheduler nodes concurrently
            await schedulerA.StartAsync(CancellationToken.None);
            await schedulerB.StartAsync(CancellationToken.None);

            // Wait for both nodes to poll and process all timers
            await Task.Delay(1000);

            await schedulerA.StopAsync(CancellationToken.None);
            await schedulerB.StopAsync(CancellationToken.None);

            // Assert
            lock (lockObj)
            {
                processedSignals.Should().HaveCount(totalTimers);
                processedSignals.Should().OnlyHaveUniqueItems();
            }
        }

        private class TestMockOrchestrator : IOrchestrator
        {
            private readonly Func<SignalDto, Task> _handler;

            public TestMockOrchestrator(Func<SignalDto, Task> handler)
            {
                _handler = handler;
            }

            public Task ProcessSignalAsync(SignalDto signalDto) => _handler(signalDto);
            public Task ProcessCommandResultAsync(CommandResultDto commandResultDto) => Task.CompletedTask;
            public Task<Guid> StartWorkflowAsync(string workflowName, int version, object input) => Task.FromResult(Guid.NewGuid());
            public Task CancelWorkflowAsync(Guid instanceId, string token, string reason = "") => Task.CompletedTask;
        }
    }
}
