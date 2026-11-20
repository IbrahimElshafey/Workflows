using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Primitives;
using Workflows.Runner.Tests.Infrastructure;
using Workflows.Storage.EntityFrameworkCore;
using Xunit;

namespace Workflows.Storage.Tests
{
    /// <summary>
    /// Guards the signal fan-out hot path: candidate instances must be loaded with a constant number of
    /// queries instead of one round trip per instance (the previous N+1 shape).
    /// </summary>
    public class InstanceStateBulkLoadTests : IDisposable
    {
        private sealed class CountingCommandInterceptor : DbCommandInterceptor
        {
            public int Count;

            public override InterceptionResult<DbDataReader> ReaderExecuting(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result)
            {
                Interlocked.Increment(ref Count);
                return base.ReaderExecuting(command, eventData, result);
            }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref Count);
                return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
            }
        }

        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<WorkflowsDbContext> _options;
        private readonly CountingCommandInterceptor _interceptor = new();
        private readonly TestObjectSerializer _serializer = new();

        public InstanceStateBulkLoadTests()
        {
            _connection = new SqliteConnection($"Data Source=BulkLoad_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            _connection.Open();

            _options = new DbContextOptionsBuilder<WorkflowsDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(_interceptor)
                .Options;

            using var context = new WorkflowsDbContext(_options);
            context.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _connection.Close();
            _connection.Dispose();
        }

        private static WorkflowStateDto CreateState(Guid id, string externalGroupId, int childCount, int index)
        {
            var group = new ExternalGroupWaitDto
            {
                Id = externalGroupId,
                WaitName = "FanOut" + index,
                WaitType = WaitType.WaitMany,
                Status = WaitStatus.Waiting,
                ChildCount = childCount,
                RequiredCompletedCount = childCount,
                Created = DateTime.UtcNow
            };

            for (var i = 0; i < childCount; i++)
            {
                group.ExternalChildWaits.Add(new SignalWaitDto
                {
                    Id = $"{externalGroupId}-child{i}",
                    WaitName = "Child" + i,
                    WaitType = WaitType.SignalWait,
                    Status = WaitStatus.Waiting,
                    SignalIdentifier = "ChildSignal" + i,
                    Created = DateTime.UtcNow
                });
            }

            return new WorkflowStateDto
            {
                Id = id,
                WorkflowType = "BulkLoadWorkflow",
                WorkflowVersion = 1,
                Status = WorkflowInstanceStatus.Running,
                Created = DateTime.UtcNow,
                Waits = new List<WaitInfrastructureDto> { group }
            };
        }

        private async Task<List<Guid>> SeedAsync(int instanceCount, int childCount = 2)
        {
            var ids = new List<Guid>();
            using var context = new WorkflowsDbContext(_options);
            var store = new WorkflowStore(context, _serializer);

            for (var i = 0; i < instanceCount; i++)
            {
                var id = Guid.NewGuid();
                ids.Add(id);
                await store.SaveContextSyncAsync(CreateState(id, "group-" + id.ToString("N"), childCount, i), Enumerable.Empty<string>());
            }

            context.ChangeTracker.Clear();
            return ids;
        }

        [Fact]
        public async Task GetInstanceStatesAsync_ShouldLoadAllInstancesWithAConstantNumberOfQueries()
        {
            var ids = await SeedAsync(4);

            using var context = new WorkflowsDbContext(_options);
            var store = new WorkflowStore(context, _serializer);

            _interceptor.Count = 0;
            var states = await store.GetInstanceStatesAsync(ids);
            var bulkQueryCount = _interceptor.Count;

            states.Should().HaveCount(4);
            states.Select(s => s.Id).Should().BeEquivalentTo(ids);
            states.Select(s => ((ExternalGroupWaitDto)s.Waits.Single()).ExternalChildWaits)
                .Should().OnlyContain(children => children.Count == 2);

            context.ChangeTracker.Clear();

            _interceptor.Count = 0;
            foreach (var id in ids)
            {
                await store.GetInstanceStateAsync(id);
            }
            var perInstanceQueryCount = _interceptor.Count;

            // One query for the instances plus one external-child hydration per instance; the per-instance
            // path costs at least that plus one FindAsync round trip each.
            bulkQueryCount.Should().Be(1 + ids.Count);
            bulkQueryCount.Should().BeLessThan(perInstanceQueryCount);
        }

        [Fact]
        public async Task GetInstanceStatesAsync_ShouldOmitMissingInstancesAndHandleEmptyInput()
        {
            var ids = await SeedAsync(2);

            using var context = new WorkflowsDbContext(_options);
            var store = new WorkflowStore(context, _serializer);

            var result = await store.GetInstanceStatesAsync(ids.Concat(new[] { Guid.NewGuid() }).ToList());

            result.Should().HaveCount(2);
            result.Select(s => s.Id).Should().BeEquivalentTo(ids);

            (await store.GetInstanceStatesAsync(Array.Empty<Guid>())).Should().BeEmpty();
        }
    }
}