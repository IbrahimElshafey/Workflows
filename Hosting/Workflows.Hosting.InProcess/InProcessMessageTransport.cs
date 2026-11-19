using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Communication.Abstraction;

namespace Workflows.Hosting.InProcess
{
    public class InProcessMessageTransport : IMessageTransport
    {
        private readonly WorkflowExecutionChannel _channel;

        public InProcessMessageTransport(WorkflowExecutionChannel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        }

        public async Task SendAsync<T>(string destination, T message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            if (message is WorkflowExecutionRequest || message is StartWorkflowRequest)
            {
                var context = new WorkflowRunContext(message, null);
                await _channel.IngressWriter.WriteAsync(context);
            }
        }

        public async Task<TResponse> SendAndReceiveAsync<TRequest, TResponse>(string destination, TRequest message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            if ((message is WorkflowExecutionRequest || message is StartWorkflowRequest) && typeof(TResponse) == typeof(AsyncResult))
            {
                var tcs = new TaskCompletionSource<AsyncResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                var context = new WorkflowRunContext(message, tcs);
                await _channel.IngressWriter.WriteAsync(context);
                var result = await tcs.Task;
                return (TResponse)(object)result;
            }
            throw new NotSupportedException($"InProcessMessageTransport does not support request of type {typeof(TRequest).Name} and response of type {typeof(TResponse).Name}");
        }
    }

    public class InProcessMessageSubscriber : IMessageSubscriber
    {
        public void Subscribe<T>(Func<T, Task> handler)
        {
            // No-op for in-process direct dispatch, since the transport calls the runner directly.
        }

        public void Dispose()
        {
        }
    }
}
