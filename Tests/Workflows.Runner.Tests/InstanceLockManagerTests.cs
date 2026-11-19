using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Abstraction.Enums;
using Workflows.Storage.EntityFrameworkCore;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class InstanceLockManagerTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<WorkflowsDbContext> _options;

        public InstanceLockManagerTests()
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
        public async Task TryAcquireLockAsync_OnUnlockedInstance_ShouldSucceed()
        {
            var instId = Guid.NewGuid();
            var nodeId = "node-1";

            using (var context = new WorkflowsDbContext(_options))
            {
                context.WorkflowInstances.Add(new WorkflowInstance
                {
                    Id = instId,
                    Status = (int)WorkflowInstanceStatus.Running,
                    Created = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }

            using var ctx = new WorkflowsDbContext(_options);
            var lockManager = new InstanceLockManager(ctx);

            var acquired = await lockManager.TryAcquireLockAsync(
                instId, nodeId, TimeSpan.FromMinutes(5));

            acquired.Should().BeTrue();

            var instance = await ctx.WorkflowInstances.FindAsync(instId);
            instance.LockedBy.Should().Be(nodeId);
            instance.LockedAt.Should().NotBeNull();
            instance.LockExpiresAt.Should().NotBeNull();
        }

        [Fact]
        public async Task TryAcquireLockAsync_OnLockedInstance_ShouldFail()
        {
            var instId = Guid.NewGuid();
            var node1 = "node-1";
            var node2 = "node-2";

            using (var context = new WorkflowsDbContext(_options))
            {
                context.WorkflowInstances.Add(new WorkflowInstance
                {
                    Id = instId,
                    Status = (int)WorkflowInstanceStatus.Running,
                    Created = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }

            using var ctx1 = new WorkflowsDbContext(_options);
            var lockManager1 = new InstanceLockManager(ctx1);
            var firstAcquired = await lockManager1.TryAcquireLockAsync(
                instId, node1, TimeSpan.FromMinutes(5));
            firstAcquired.Should().BeTrue();

            using var ctx2 = new WorkflowsDbContext(_options);
            var lockManager2 = new InstanceLockManager(ctx2);
            var secondAcquired = await lockManager2.TryAcquireLockAsync(
                instId, node2, TimeSpan.FromMinutes(5));
            secondAcquired.Should().BeFalse();
        }

        [Fact]
        public async Task TryAcquireLockAsync_SameNode_ShouldRefreshTtl()
        {
            var instId = Guid.NewGuid();
            var nodeId = "node-1";

            using (var context = new WorkflowsDbContext(_options))
            {
                context.WorkflowInstances.Add(new WorkflowInstance
                {
                    Id = instId,
                    Status = (int)WorkflowInstanceStatus.Running,
                    Created = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }

            using var ctx = new WorkflowsDbContext(_options);
            var lockManager = new InstanceLockManager(ctx);

            var firstAcquired = await lockManager.TryAcquireLockAsync(
                instId, nodeId, TimeSpan.FromMinutes(1));
            firstAcquired.Should().BeTrue();

            var firstInstance = await ctx.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instId);
            var firstExpiry = firstInstance.LockExpiresAt!.Value;

            await Task.Delay(100);

            var secondAcquired = await lockManager.TryAcquireLockAsync(
                instId, nodeId, TimeSpan.FromMinutes(5));
            secondAcquired.Should().BeTrue();

            var secondInstance = await ctx.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instId);
            secondInstance.LockExpiresAt.Should().BeAfter(firstExpiry);
        }

        [Fact]
        public async Task ReleaseLockAsync_ByOwner_ShouldClearLock()
        {
            var instId = Guid.NewGuid();
            var nodeId = "node-1";

            using (var context = new WorkflowsDbContext(_options))
            {
                context.WorkflowInstances.Add(new WorkflowInstance
                {
                    Id = instId,
                    Status = (int)WorkflowInstanceStatus.Running,
                    Created = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }

            using var ctx = new WorkflowsDbContext(_options);
            var lockManager = new InstanceLockManager(ctx);

            await lockManager.TryAcquireLockAsync(instId, nodeId, TimeSpan.FromMinutes(5));
            await lockManager.ReleaseLockAsync(instId, nodeId);

            var instance = await ctx.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instId);
            instance.LockedBy.Should().BeNull();
            instance.LockedAt.Should().BeNull();
            instance.LockExpiresAt.Should().BeNull();
        }

        [Fact]
        public async Task ReleaseLockAsync_ByNonOwner_ShouldNotClearLock()
        {
            var instId = Guid.NewGuid();
            var node1 = "node-1";
            var node2 = "node-2";

            using (var context = new WorkflowsDbContext(_options))
            {
                context.WorkflowInstances.Add(new WorkflowInstance
                {
                    Id = instId,
                    Status = (int)WorkflowInstanceStatus.Running,
                    Created = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }

            using var ctx = new WorkflowsDbContext(_options);
            var lockManager = new InstanceLockManager(ctx);

            await lockManager.TryAcquireLockAsync(instId, node1, TimeSpan.FromMinutes(5));
            await lockManager.ReleaseLockAsync(instId, node2);

            var instance = await ctx.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instId);
            instance.LockedBy.Should().Be(node1);
        }

        [Fact]
        public async Task TryAcquireLockAsync_OnExpiredLock_ShouldSucceed()
        {
            var instId = Guid.NewGuid();
            var node1 = "node-1";
            var node2 = "node-2";

            using (var context = new WorkflowsDbContext(_options))
            {
                context.WorkflowInstances.Add(new WorkflowInstance
                {
                    Id = instId,
                    Status = (int)WorkflowInstanceStatus.Running,
                    Created = DateTime.UtcNow,
                    LockedBy = node1,
                    LockedAt = DateTime.UtcNow.AddMinutes(-10),
                    LockExpiresAt = DateTime.UtcNow.AddMinutes(-5) // Already expired
                });
                await context.SaveChangesAsync();
            }

            using var ctx = new WorkflowsDbContext(_options);
            var lockManager = new InstanceLockManager(ctx);

            var acquired = await lockManager.TryAcquireLockAsync(
                instId, node2, TimeSpan.FromMinutes(5));

            acquired.Should().BeTrue();

            var instance = await ctx.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instId);
            instance.LockedBy.Should().Be(node2);
        }

        [Fact]
        public async Task ReleaseExpiredLocksAsync_ShouldClearAllExpiredLocks()
        {
            var instId1 = Guid.NewGuid();
            var instId2 = Guid.NewGuid();
            var instId3 = Guid.NewGuid();

            using (var context = new WorkflowsDbContext(_options))
            {
                context.WorkflowInstances.AddRange(
                    new WorkflowInstance
                    {
                        Id = instId1,
                        Status = (int)WorkflowInstanceStatus.Running,
                        Created = DateTime.UtcNow,
                        LockedBy = "node-1",
                        LockedAt = DateTime.UtcNow.AddMinutes(-10),
                        LockExpiresAt = DateTime.UtcNow.AddMinutes(-5) // Expired
                    },
                    new WorkflowInstance
                    {
                        Id = instId2,
                        Status = (int)WorkflowInstanceStatus.Running,
                        Created = DateTime.UtcNow,
                        LockedBy = "node-2",
                        LockedAt = DateTime.UtcNow,
                        LockExpiresAt = DateTime.UtcNow.AddMinutes(5) // Not expired
                    },
                    new WorkflowInstance
                    {
                        Id = instId3,
                        Status = (int)WorkflowInstanceStatus.Running,
                        Created = DateTime.UtcNow,
                        LockedBy = "node-3",
                        LockedAt = DateTime.UtcNow.AddMinutes(-20),
                        LockExpiresAt = DateTime.UtcNow.AddMinutes(-15) // Expired
                    }
                );
                await context.SaveChangesAsync();
            }

            using var ctx = new WorkflowsDbContext(_options);
            var lockManager = new InstanceLockManager(ctx);

            await lockManager.ReleaseExpiredLocksAsync();

            var inst1 = await ctx.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instId1);
            var inst2 = await ctx.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instId2);
            var inst3 = await ctx.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instId3);

            inst1.LockedBy.Should().BeNull();
            inst2.LockedBy.Should().Be("node-2");
            inst3.LockedBy.Should().BeNull();
        }

        [Fact]
        public async Task GetExpiredLocksAsync_ShouldReturnOnlyExpiredInstanceIds()
        {
            var instId1 = Guid.NewGuid();
            var instId2 = Guid.NewGuid();
            var instId3 = Guid.NewGuid();

            using (var context = new WorkflowsDbContext(_options))
            {
                context.WorkflowInstances.AddRange(
                    new WorkflowInstance
                    {
                        Id = instId1,
                        Status = (int)WorkflowInstanceStatus.Running,
                        Created = DateTime.UtcNow,
                        LockedBy = "node-1",
                        LockedAt = DateTime.UtcNow.AddMinutes(-10),
                        LockExpiresAt = DateTime.UtcNow.AddMinutes(-5) // Expired
                    },
                    new WorkflowInstance
                    {
                        Id = instId2,
                        Status = (int)WorkflowInstanceStatus.Running,
                        Created = DateTime.UtcNow,
                        LockedBy = "node-2",
                        LockedAt = DateTime.UtcNow,
                        LockExpiresAt = DateTime.UtcNow.AddMinutes(5) // Not expired
                    },
                    new WorkflowInstance
                    {
                        Id = instId3,
                        Status = (int)WorkflowInstanceStatus.Running,
                        Created = DateTime.UtcNow,
                        LockedBy = "node-3",
                        LockedAt = DateTime.UtcNow.AddMinutes(-20),
                        LockExpiresAt = DateTime.UtcNow.AddMinutes(-15) // Expired
                    }
                );
                await context.SaveChangesAsync();
            }

            using var ctx = new WorkflowsDbContext(_options);
            var lockManager = new InstanceLockManager(ctx);

            var expiredIds = await lockManager.GetExpiredLocksAsync();

            expiredIds.Should().HaveCount(2);
            expiredIds.Should().Contain(new[] { instId1, instId3 });
        }

        [Fact]
        public async Task TryAcquireLockAsync_OnNonExistentInstance_ShouldReturnFalse()
        {
            var instId = Guid.NewGuid();

            using var ctx = new WorkflowsDbContext(_options);
            var lockManager = new InstanceLockManager(ctx);

            var acquired = await lockManager.TryAcquireLockAsync(
                instId, "node-1", TimeSpan.FromMinutes(5));

            acquired.Should().BeFalse();
        }
    }
}