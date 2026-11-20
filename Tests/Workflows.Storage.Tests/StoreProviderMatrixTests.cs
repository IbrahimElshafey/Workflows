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
using Workflows.Runner.Tests.Infrastructure;
using Workflows.Primitives;
using Workflows.Storage.EntityFrameworkCore;
using Xunit;

namespace Workflows.Storage.Tests
{
    /// <summary>
    /// Runs the same store round trip against every provider the build can talk to. SQLite always runs;
    /// Postgres and SQL Server join the matrix when <c>WORKFLOWS_TEST_POSTGRES</c> /
    /// <c>WORKFLOWS_TEST_SQLSERVER</c> hold a connection string, so CI can add them without skipping the
    /// rest of the suite. Each provider is expected to own a disposable database.
    /// </summary>
    public class StoreProviderMatrixTests
    {
        private const string PostgresVariable = "WORKFLOWS_TEST_POSTGRES";
        private const string SqlServerVariable = "WORKFLOWS_TEST_SQLSERVER";

        public static TheoryData<string> Providers
        {
            get
            {
                var data = new TheoryData<string> { "sqlite" };
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresVariable)))
                {
                    data.Add("postgres");
                }

                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerVariable)))
                {
                    data.Add("sqlserver");
                }

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public async Task Store_PersistsAndReloadsState(string provider)
        {
            using var connection = provider == "sqlite"
                ? new SqliteConnection($"Data Source=Matrix_{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
                : null;
            connection?.Open();

            var options = BuildOptions(provider, connection);
            await using var context = new WorkflowsDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var store = new WorkflowStore(context, new TestObjectSerializer());
            var instanceId = Guid.NewGuid();
            var state = new WorkflowStateDto
            {
                Id = instanceId,
                WorkflowType = "MatrixWorkflow",
                WorkflowVersion = 1,
                Status = WorkflowInstanceStatus.Running,
                Created = DateTime.UtcNow,
                Waits = new List<WaitInfrastructureDto>
                {
                    new SignalWaitDto
                    {
                        Id = $"{instanceId}/signal",
                        SignalIdentifier = "OrderReceived",
                        WaitName = "Wait for order",
                        Status = WaitStatus.Waiting,
                        WaitType = WaitType.SignalWait
                    }
                }
            };

            await store.SaveContextSyncAsync(state, Array.Empty<string>());

            var reloaded = await store.GetInstanceStateAsync(instanceId);

            reloaded.Should().NotBeNull();
            reloaded.WorkflowType.Should().Be("MatrixWorkflow");
            reloaded.Status.Should().Be(WorkflowInstanceStatus.Running);
            reloaded.Waits.Should().ContainSingle(w => w.WaitName == "Wait for order");

            // Second save must update the existing row instead of inserting a duplicate.
            state.Status = WorkflowInstanceStatus.Completed;
            await store.SaveContextSyncAsync(state, new[] { $"{instanceId}/signal" });

            var updated = await store.GetInstanceStateAsync(instanceId);
            updated.Status.Should().Be(WorkflowInstanceStatus.Completed);

            await RemoveInstanceAsync(context, instanceId);
        }

        private static DbContextOptions<WorkflowsDbContext> BuildOptions(string provider, SqliteConnection? connection)
        {
            var builder = new DbContextOptionsBuilder<WorkflowsDbContext>();
            switch (provider)
            {
                case "sqlite":
                    builder.UseSqlite(connection!);
                    break;
                case "postgres":
                    builder.UseNpgsql(Environment.GetEnvironmentVariable(PostgresVariable));
                    break;
                case "sqlserver":
                    builder.UseSqlServer(Environment.GetEnvironmentVariable(SqlServerVariable));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider.");
            }

            return builder.Options;
        }

        private static async Task RemoveInstanceAsync(WorkflowsDbContext context, Guid instanceId)
        {
            var instance = await context.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == instanceId);
            if (instance == null) return;

            context.WorkflowInstances.Remove(instance);
            await context.SaveChangesAsync();
        }
    }
}