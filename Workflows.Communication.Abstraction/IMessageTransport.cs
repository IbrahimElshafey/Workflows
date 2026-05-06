using System.Threading.Tasks;

namespace Workflows.Communication.Abstraction
{
    public interface IMessageTransport
    {
        Task SendAsync<T>(string destination, T message);
        Task<TResponse> SendAndReceiveAsync<TRequest, TResponse>(string destination, TRequest message);
    }
}