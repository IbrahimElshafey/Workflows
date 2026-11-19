using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Workflows.Abstraction.Persistence
{
    /// <summary>
    /// Manages distributed instance-level locks to prevent concurrent processing
    /// of the same workflow instance by multiple nodes.
    /// </summary>
    public interface IInstanceLockManager
    {
        /// <summary>
        /// Attempts to acquire a lock on the specified workflow instance.
        /// Returns true if the lock was acquired; false if another node holds a valid lock.
        /// </summary>
        /// <param name="instanceId">The workflow instance ID to lock.</param>
        /// <param name="nodeId">The identifier of the node requesting the lock.</param>
        /// <param name="ttl">How long the lock is valid before it expires.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<bool> TryAcquireLockAsync(Guid instanceId, string nodeId, TimeSpan ttl, CancellationToken ct = default);

        /// <summary>
        /// Releases the lock on the specified workflow instance, but only if the
        /// calling node is the current owner.
        /// </summary>
        /// <param name="instanceId">The workflow instance ID to unlock.</param>
        /// <param name="nodeId">The identifier of the node releasing the lock.</param>
        /// <param name="ct">Cancellation token.</param>
        Task ReleaseLockAsync(Guid instanceId, string nodeId, CancellationToken ct = default);

        /// <summary>
        /// Releases all locks whose TTL has expired, making them available for acquisition.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        Task ReleaseExpiredLocksAsync(CancellationToken ct = default);

        /// <summary>
        /// Returns the list of instance IDs whose locks have expired but have not yet been released.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        Task<List<Guid>> GetExpiredLocksAsync(CancellationToken ct = default);
    }
}