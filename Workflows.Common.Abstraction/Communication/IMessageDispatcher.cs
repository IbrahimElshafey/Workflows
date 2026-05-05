using System.Threading.Tasks;
namespace Workflows.Shared.Communication
{
    public interface IMessageDispatcher
    {
        // Fire and Forget (e.g., triggering a background command)
        Task DispatchAsync<T>(T message);

        // Request-Response (e.g., getting the SyncResult during registration)
        Task<TResponse> DispatchAndReceiveAsync<TRequest, TResponse>(TRequest message);
    }
}