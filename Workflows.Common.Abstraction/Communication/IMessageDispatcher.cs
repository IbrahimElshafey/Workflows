
using System;
using System.Linq;
using System.Threading.Tasks;




namespace Workflows.Common.Abstraction.Communication
{
    public interface IMessageDispatcher
    {
        // Fire and Forget (e.g., triggering a background command)
        Task DispatchAsync<T>(T message);

        // Request-Response (e.g., getting the SyncResult during registration)
        Task<TResponse> DispatchAndReceiveAsync<TRequest, TResponse>(TRequest message);
    }
}