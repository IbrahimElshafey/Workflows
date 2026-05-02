
using System;
using System.Linq;
using System.Threading.Tasks;
namespace Workflows.Abstraction.Communication
{
    public interface IMessageTransport
    {
        Task SendAsync<T>(string destination, T message);
        Task<TResponse> SendAndReceiveAsync<TRequest, TResponse>(string destination, TRequest message);
    }
}