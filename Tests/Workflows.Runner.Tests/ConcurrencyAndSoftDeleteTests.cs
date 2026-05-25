using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Storage.EntityFrameworkCore;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class ConcurrencyAndSoftDeleteTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<WorkflowsDbContext> _options;
        private readonly WorkflowAuditingInterceptor _interceptor;

        public ConcurrencyAndSoftDeleteTests()
        {
            // Use SQLite in-memory database with shared cache for the duration of the test run
            _connection = new SqliteConnection($"Data Source=InMemory_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            _connection.Open();

            _interceptor = new WorkflowAuditingInterceptor();

            _options = new DbContextOptionsBuilder<WorkflowsDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(_interceptor)
                .Options;

            // Initialize database schema
            using var context = new WorkflowsDbContext(_options);
            context.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _connection.Close();
            _connection.Dispose();
        }

        [Fact]
        public async Task ConcurrencyToken_ShouldThrowDbUpdateConcurrencyException_OnConflict()
        {
            var instanceId = Guid.NewGuid();

            // 1. Arrange: Insert a workflow instance
            using (var context = new WorkflowsDbContext(_options))
            {
                var instance = new WorkflowInstance
                {
                    Id = instanceId,
                    Status = 1, // Running
                    WorkflowType = "TestWorkflow",
                    Created = DateTime.UtcNow,
                    StateObject = new WorkflowStateObject { StateIndex = 0 }
                };
                context.WorkflowInstances.Add(instance);
                await context.SaveChangesAsync();
            }

            // 2. Act: Fetch in two different contexts simulating two independent Runner nodes
            using var contextScope1 = new WorkflowsDbContext(_options);
            using var contextScope2 = new WorkflowsDbContext(_options);

            var instance1 = await contextScope1.WorkflowInstances.FindAsync(instanceId);
            var instance2 = await contextScope2.WorkflowInstances.FindAsync(instanceId);

            instance1.Should().NotBeNull();
            instance2.Should().NotBeNull();
            instance1!.ConcurrencyToken.Should().Be(instance2!.ConcurrencyToken);

            // Modify and save first instance (advancing token in database)
            instance1.Status = 2; // Completed
            await contextScope1.SaveChangesAsync();

            // Modify second instance (using stale token) and try to save
            instance2.Status = 3; // Cancelled
            
            // 3. Assert: Throws DbUpdateConcurrencyException
            Func<Task> saveAction = () => contextScope2.SaveChangesAsync();
            await saveAction.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        [Fact]
        public async Task SoftDelete_ShouldMarkRowAsDeleted_AndFilterFromDefaultQueries()
        {
            var instanceId = Guid.NewGuid();
            var waitId = Guid.NewGuid();

            // 1. Arrange: Save a workflow instance and an active wait record
            using (var context = new WorkflowsDbContext(_options))
            {
                var instance = new WorkflowInstance
                {
                    Id = instanceId,
                    Status = 1,
                    WorkflowType = "TestWorkflow",
                    Created = DateTime.UtcNow
                };
                context.WorkflowInstances.Add(instance);

                var wait = new SignalWait
                {
                    Id = waitId,
                    WorkflowInstanceId = instanceId,
                    Status = 1, // Waiting
                    WaitName = "TestSignalWait",
                    WaitType = 1,
                    SignalPath = "some/signal/path",
                    Created = DateTime.UtcNow
                };
                context.WorkflowWaits.Add(wait);
                await context.SaveChangesAsync();
            }

            // 2. Act: Soft-delete the wait record
            using (var context = new WorkflowsDbContext(_options))
            {
                var wait = await context.WorkflowWaits.FindAsync(waitId);
                wait.Should().NotBeNull();
                
                context.WorkflowWaits.Remove(wait!);
                await context.SaveChangesAsync();
            }

            // 3. Assert: Verify the wait record is filtered by default queries,
            // but is still present in the database with IsDeleted = true.
            using (var context = new WorkflowsDbContext(_options))
            {
                // Default query should filter it out
                var activeWait = await context.WorkflowWaits.FindAsync(waitId);
                activeWait.Should().BeNull();

                var activeWaitsList = await context.WorkflowWaits
                    .Where(w => w.WorkflowInstanceId == instanceId)
                    .ToListAsync();
                activeWaitsList.Should().BeEmpty();

                // Ignoring query filters should retrieve the soft-deleted row
                var softDeletedWait = await context.WorkflowWaits
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(w => w.Id == waitId);

                softDeletedWait.Should().NotBeNull();
                softDeletedWait!.IsDeleted.Should().BeTrue();
                softDeletedWait.WaitName.Should().Be("TestSignalWait");
            }
        }
    }
}
