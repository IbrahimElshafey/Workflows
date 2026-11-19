using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Abstraction.Persistence;

namespace Workflows.Storage.EntityFrameworkCore
{
    /// <summary>
    /// EF Core implementation of <see cref="IInstanceLockManager"/>.
    /// Uses atomic conditional UPDATE statements to prevent race conditions
    /// when multiple nodes attempt to lock the same workflow instance.
    /// </summary>
    public class InstanceLockManager : IInstanceLockManager
    {
        private readonly WorkflowsDbContext _dbContext;

        public InstanceLockManager(WorkflowsDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        /// <inheritdoc/>
        public async Task<bool> TryAcquireLockAsync(
            Guid instanceId,
            string nodeId,
            TimeSpan ttl,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));

            var now = DateTime.UtcNow;
            var expiresAt = now.Add(ttl);

            // Atomic conditional UPDATE: acquire the lock only if no one holds it
            // or the existing lock has expired.
            //
            // SQL (EF Core translated):
            //   UPDATE WorkflowInstances
            //   SET LockedBy = @nodeId, LockedAt = @now, LockExpiresAt = @expiresAt
            //   WHERE Id = @instanceId
            //     AND (LockedBy IS NULL OR LockExpiresAt IS NULL OR LockExpiresAt < @now)
            //
            // If the calling node already owns the lock (re-entrancy), we refresh the TTL.
            var affected = await _dbContext.WorkflowInstances
                .Where(i => i.Id == instanceId
                            && (i.LockedBy == null
                                || i.LockExpiresAt == null
                                || i.LockExpiresAt < now
                                || i.LockedBy == nodeId))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(i => i.LockedBy, nodeId)
                          .SetProperty(i => i.LockedAt, now)
                          .SetProperty(i => i.LockExpiresAt, expiresAt),
                    ct);

            return affected > 0;
        }

        /// <inheritdoc/>
        public async Task ReleaseLockAsync(
            Guid instanceId,
            string nodeId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));

            // Only release if the calling node is the current owner.
            await _dbContext.WorkflowInstances
                .Where(i => i.Id == instanceId && i.LockedBy == nodeId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(i => i.LockedBy, (string?)null)
                          .SetProperty(i => i.LockedAt, (DateTime?)null)
                          .SetProperty(i => i.LockExpiresAt, (DateTime?)null),
                    ct);
        }

        /// <inheritdoc/>
        public async Task ReleaseExpiredLocksAsync(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;

            await _dbContext.WorkflowInstances
                .Where(i => i.LockedBy != null
                            && i.LockExpiresAt != null
                            && i.LockExpiresAt < now)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(i => i.LockedBy, (string?)null)
                          .SetProperty(i => i.LockedAt, (DateTime?)null)
                          .SetProperty(i => i.LockExpiresAt, (DateTime?)null),
                    ct);
        }

        /// <inheritdoc/>
        public async Task<List<Guid>> GetExpiredLocksAsync(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;

            return await _dbContext.WorkflowInstances
                .Where(i => i.LockedBy != null
                            && i.LockExpiresAt != null
                            && i.LockExpiresAt < now)
                .Select(i => i.Id)
                .ToListAsync(ct);
        }
    }
}