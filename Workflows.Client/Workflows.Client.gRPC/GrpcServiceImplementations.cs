using System;
using System.Threading.Tasks;
using Grpc.Core;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Orchestrator;
using Workflows.Client.Grpc;

namespace Workflows.Client.gRPC
{
    /// <summary>
    /// Server-side gRPC service that exposes endpoints for incoming workflow triggers
    /// and routes them to the <see cref="IOrchestrator"/>.
    /// </summary>
    public class GrpcWorkflowOrchestratorService : WorkflowOrchestrator.WorkflowOrchestratorBase
    {
        private readonly IOrchestrator _orchestrator;
        private readonly IObjectSerializer _serializer;

        public GrpcWorkflowOrchestratorService(IOrchestrator orchestrator, IObjectSerializer serializer)
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public override async Task<SignalResponse> ProcessSignal(SignalRequest request, ServerCallContext context)
        {
            try
            {
                var signalDto = new SignalDto
                {
                    Id = Guid.Parse(request.Id),
                    SignalIdentifier = request.SignalIdentifier,
                    Data = string.IsNullOrEmpty(request.JsonData) ? null! : request.JsonData, // Pass raw JSON string; Orchestrator will deserialize
                    ClientSentTime = DateTime.Parse(request.ClientSentTime, null, System.Globalization.DateTimeStyles.RoundtripKind),
                    OrchestratorReceiveTime = DateTime.UtcNow
                };

                await _orchestrator.ProcessSignalAsync(signalDto).ConfigureAwait(false);
                return new SignalResponse { Success = true };
            }
            catch (Exception ex)
            {
                return new SignalResponse { Success = false, ErrorMessage = ex.Message };
            }
        }

        public override async Task<CommandResultResponse> ProcessCommandResult(CommandResultRequest request, ServerCallContext context)
        {
            try
            {
                var resultDto = new CommandResultDto
                {
                    CommandWaitId = request.CommandWaitId,
                    Result = string.IsNullOrEmpty(request.JsonResult) ? null! : request.JsonResult, // Pass raw JSON string; Orchestrator will deserialize
                    ClientSentTime = DateTime.Parse(request.ClientSentTime, null, System.Globalization.DateTimeStyles.RoundtripKind),
                    OrchestratorReceiveTime = DateTime.UtcNow
                };

                await _orchestrator.ProcessCommandResultAsync(resultDto).ConfigureAwait(false);
                return new CommandResultResponse { Success = true };
            }
            catch (Exception ex)
            {
                return new CommandResultResponse { Success = false, ErrorMessage = ex.Message };
            }
        }

        public override async Task<StartWorkflowResponse> StartWorkflow(Workflows.Client.Grpc.StartWorkflowRequest request, ServerCallContext context)
        {
            try
            {
                var inputObj = string.IsNullOrEmpty(request.JsonInput) ? null! : _serializer.Deserialize<object>(request.JsonInput);
                var workflowId = await _orchestrator.StartWorkflowAsync(request.WorkflowName, request.Version, inputObj).ConfigureAwait(false);
                return new StartWorkflowResponse { Success = true, WorkflowId = workflowId.ToString() };
            }
            catch (Exception ex)
            {
                return new StartWorkflowResponse { Success = false, ErrorMessage = ex.Message };
            }
        }
    }

    /// <summary>
    /// Client-side gRPC service that exposes endpoints for incoming command executions
    /// and routes them to the <see cref="GrpcWorkflowMessageSubscriber"/>.
    /// </summary>
    public class GrpcWorkflowExecutorService : WorkflowExecutor.WorkflowExecutorBase
    {
        private readonly GrpcWorkflowMessageSubscriber _subscriber;

        public GrpcWorkflowExecutorService(GrpcWorkflowMessageSubscriber subscriber)
        {
            _subscriber = subscriber ?? throw new ArgumentNullException(nameof(subscriber));
        }

        public override async Task<CommandDispatchResponse> DispatchCommand(CommandDispatchRequest request, ServerCallContext context)
        {
            try
            {
                var notification = new CommandDispatchNotification
                {
                    CommandWaitId = request.CommandWaitId,
                    HandlerKey = request.HandlerKey,
                    CommandData = string.IsNullOrEmpty(request.JsonData) ? null! : request.JsonData, // Pass JSON string directly to defer typed deserialization
                    DispatchedAt = DateTime.Parse(request.DispatchedAt, null, System.Globalization.DateTimeStyles.RoundtripKind)
                };

                await _subscriber.HandleMessageAsync(typeof(CommandDispatchNotification), notification).ConfigureAwait(false);
                return new CommandDispatchResponse { Success = true };
            }
            catch (Exception ex)
            {
                return new CommandDispatchResponse { Success = false, ErrorMessage = ex.Message };
            }
        }
    }
}
