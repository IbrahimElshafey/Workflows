using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Persistence;

namespace Workflows.Hosting.InProcess
{
    public class WorkflowInstanceCache : IWorkflowInstanceCache
    {
        private readonly ConcurrentDictionary<Guid, (WorkflowStateDto State, DateTime LastAccess)> _cache = new();
        private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

        public async Task<WorkflowStateDto?> GetOrAddAsync(Guid instanceId, Func<Guid, Task<WorkflowStateDto?>> valueFactory)
        {
            if (_cache.TryGetValue(instanceId, out var entry))
            {
                _cache[instanceId] = (entry.State, DateTime.UtcNow);
                return entry.State;
            }

            var state = await valueFactory(instanceId);
            if (state != null)
            {
                _cache[instanceId] = (state, DateTime.UtcNow);
            }
            return state;
        }

        public void Remove(Guid instanceId)
        {
            _cache.TryRemove(instanceId, out _);
        }

        public void Update(Guid instanceId, WorkflowStateDto state)
        {
            if (state == null) return;
            _cache[instanceId] = (state, DateTime.UtcNow);
        }

        public async Task<T> AcquireLockAsync<T>(Guid instanceId, Func<Task<T>> action)
        {
            var sem = _locks.GetOrAdd(instanceId, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync();
            try
            {
                return await action();
            }
            finally
            {
                sem.Release();
            }
        }
    }
}
