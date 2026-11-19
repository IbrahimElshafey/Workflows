using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Helpers;
using Workflows.Communication.Abstraction;
using Workflows.Client.Grpc;

namespace Workflows.Client.gRPC
{
    /// <summary>
    /// Implements <see cref="IMessageTransport"/> over gRPC.
    /// Maps and sends messages to gRPC services based on the <c>destination</c> channel address.
    /// </summary>
    public class GrpcWorkflowMessageTransport : IMessageTransport, IDisposable
    {
        private readonly IObjectSerializer _serializer;
        private readonly ConcurrentDictionary<string, GrpcChannel> _channels = new(StringComparer.OrdinalIgnoreCase);

        public GrpcWorkflowMessageTransport(IObjectSerializer serializer)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        protected virtual GrpcChannel GetChannel(string destination)
        {
            if (string.IsNullOrWhiteSpace(destination))
                throw new ArgumentNullException(nameof(destination));

            return _channels.GetOrAdd(destination, addr => GrpcChannel.ForAddress(addr));
        }

        public async Task SendAsync<T>(string destination, T message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            var channel = GetChannel(destination);

            if (message is SignalDto signal)
            {
                var client = new WorkflowOrchestrator.WorkflowOrchestratorClient(channel);
                var dataPayload = _serializer.Serialize(signal.Data);
                var request = new SignalRequest
                {
                    Id = signal.Id.ToString(),
                    SignalIdentifier = signal.SignalIdentifier,
                    JsonData = dataPayload as string ?? dataPayload?.ToString() ?? string.Empty,
                    ClientSentTime = signal.ClientSentTime.ToString("o")
                };

                var response = await client.ProcessSignalAsync(request).ConfigureAwait(false);
                if (!response.Success)
                {
                    throw new InvalidOperationException($"ProcessSignal failed: {response.ErrorMessage}");
                }
            }
            else if (message is CommandResultDto commandResult)
            {
                var client = new WorkflowOrchestrator.WorkflowOrchestratorClient(channel);
                var resultPayload = _serializer.Serialize(commandResult.Result);
                var request = new CommandResultRequest
                {
                    CommandWaitId = commandResult.CommandWaitId.ToString(),
                    JsonResult = resultPayload as string ?? resultPayload?.ToString() ?? string.Empty,
                    ClientSentTime = commandResult.ClientSentTime.ToString("o")
                };

                var response = await client.ProcessCommandResultAsync(request).ConfigureAwait(false);
                if (!response.Success)
                {
                    throw new InvalidOperationException($"ProcessCommandResult failed: {response.ErrorMessage}");
                }
            }
            else if (message is CommandDispatchNotification dispatchNotification)
            {
                var client = new WorkflowExecutor.WorkflowExecutorClient(channel);
                var commandPayload = dispatchNotification.CommandData;
                var request = new CommandDispatchRequest
                {
                    CommandWaitId = dispatchNotification.CommandWaitId.ToString(),
                    HandlerKey = dispatchNotification.HandlerKey,
                    JsonData = (commandPayload as string) ?? commandPayload?.ToString() ?? string.Empty,
                    DispatchedAt = dispatchNotification.DispatchedAt.ToString("o")
                };

                var response = await client.DispatchCommandAsync(request).ConfigureAwait(false);
                if (!response.Success)
                {
                    throw new InvalidOperationException($"DispatchCommand failed: {response.ErrorMessage}");
                }
            }
            else
            {
                throw new NotSupportedException($"GrpcWorkflowMessageTransport does not support one-way send for type {typeof(T).Name}");
            }
        }

        public async Task<TResponse> SendAndReceiveAsync<TRequest, TResponse>(string destination, TRequest message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            var channel = GetChannel(destination);

            if (message is Workflows.Abstraction.DTOs.StartWorkflowRequest startReq && typeof(TResponse) == typeof(AsyncResult))
            {
                var client = new WorkflowOrchestrator.WorkflowOrchestratorClient(channel);
                var inputPayload = startReq.Input != null ? _serializer.Serialize(startReq.Input) : null;
                
                var request = new Workflows.Client.Grpc.StartWorkflowRequest
                {
                    WorkflowName = startReq.WorkflowName,
                    Version = 1, // Default version
                    JsonInput = inputPayload as string ?? inputPayload?.ToString() ?? string.Empty
                };

                var response = await client.StartWorkflowAsync(request).ConfigureAwait(false);
                if (!response.Success)
                {
                    throw new InvalidOperationException($"StartWorkflow failed: {response.ErrorMessage}");
                }

                var instanceId = Guid.Parse(response.WorkflowId);
                var asyncResult = new AsyncResult(
                    instanceId,
                    new object(),
                    "Accepted",
                    "Workflow started successfully via gRPC",
                    DateTime.UtcNow
                );

                return (TResponse)(object)asyncResult;
            }

            throw new NotSupportedException($"GrpcWorkflowMessageTransport does not support request-response for request {typeof(TRequest).Name} and response {typeof(TResponse).Name}");
        }

        public void Dispose()
        {
            foreach (var channel in _channels.Values)
            {
                channel.Dispose();
            }
            _channels.Clear();
        }
    }
}
