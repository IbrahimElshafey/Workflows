using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;
using Workflows.Communication.Abstraction;
using Workflows.Runner;

namespace Workflows.Hosting.InProcess
{
    public class InProcessMessageTransport : IMessageTransport
    {
        private readonly IServiceProvider _serviceProvider;

        public InProcessMessageTransport(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public Task SendAsync<T>(string destination, T message)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
                        if (message is WorkflowExecutionRequest req)
                        {
                            await runner.RunWorkflowAsync(req);
                        }
                        else if (message is StartWorkflowRequest startReq)
                        {
                            await runner.StartWorkflow(startReq.WorkflowName, startReq.Input);
                        }
                    }
                }
                catch (Exception)
                {
                    // Handle or log error in background
                }
            });
            return Task.CompletedTask;
        }

        public async Task<TResponse> SendAndReceiveAsync<TRequest, TResponse>(string destination, TRequest message)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var runner = scope.ServiceProvider.GetRequiredService<WorkflowRunner>();
                if (message is WorkflowExecutionRequest req && typeof(TResponse) == typeof(AsyncResult))
                {
                    var result = await runner.RunWorkflowAsync(req);
                    return (TResponse)(object)result;
                }
                if (message is StartWorkflowRequest startReq && typeof(TResponse) == typeof(AsyncResult))
                {
                    var result = await runner.StartWorkflow(startReq.WorkflowName, startReq.Input);
                    return (TResponse)(object)result;
                }
                throw new NotSupportedException($"InProcessMessageTransport does not support request of type {typeof(TRequest).Name} and response of type {typeof(TResponse).Name}");
            }
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
