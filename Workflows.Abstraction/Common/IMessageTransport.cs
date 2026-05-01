
using System;
using System.Threading.Tasks;

namespace Workflows.Abstraction.Common
{
    public interface IMessageTransport
    {
        /// <summary>
        /// Sends a message to a specific destination. 
        /// The destination could be a Queue Name, a Topic, or a URL.
        /// </summary>
        Task SendAsync<T>(string destination, T message);

        /// <summary>
        /// For Request-Response patterns (like API or gRPC).
        /// </summary>
        Task<TResponse> SendAndReceiveAsync<TRequest, TResponse>(string destination, TRequest message);
    }
}