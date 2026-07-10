using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;

namespace Workflows.Abstraction.Persistence
{
    public interface IWorkflowInstanceCache
    {
        Task<WorkflowStateDto?> GetOrAddAsync(Guid instanceId, Func<Guid, Task<WorkflowStateDto?>> valueFactory);
        void Remove(Guid instanceId);
        void Update(Guid instanceId, WorkflowStateDto state);
        Task<T> AcquireLockAsync<T>(Guid instanceId, Func<Task<T>> action);
    }
}
