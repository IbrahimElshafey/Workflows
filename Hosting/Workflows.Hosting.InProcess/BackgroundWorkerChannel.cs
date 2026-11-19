using System;
using System.Threading.Channels;
using Workflows.Abstraction.DTOs;

namespace Workflows.Hosting.InProcess
{
    public class BackgroundWorkerChannel
    {
        private readonly Channel<CommandDispatchNotification> _commandChannel;
        private readonly Channel<CompensationRequest> _compensationChannel;
        private readonly Channel<CancellationRequest> _cancellationChannel;

        public ChannelReader<CommandDispatchNotification> CommandReader => _commandChannel.Reader;
        public ChannelWriter<CommandDispatchNotification> CommandWriter => _commandChannel.Writer;

        public ChannelReader<CompensationRequest> CompensationReader => _compensationChannel.Reader;
        public ChannelWriter<CompensationRequest> CompensationWriter => _compensationChannel.Writer;

        public ChannelReader<CancellationRequest> CancellationReader => _cancellationChannel.Reader;
        public ChannelWriter<CancellationRequest> CancellationWriter => _cancellationChannel.Writer;

        public BackgroundWorkerChannel(int capacity = 1000)
        {
            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };

            _commandChannel = Channel.CreateBounded<CommandDispatchNotification>(options);
            _compensationChannel = Channel.CreateBounded<CompensationRequest>(options);
            _cancellationChannel = Channel.CreateBounded<CancellationRequest>(options);
        }
    }

    public class CompensationRequest
    {
        public Guid WorkflowInstanceId { get; set; }
    }

    public class CancellationRequest
    {
        public Guid WorkflowInstanceId { get; set; }
        public string Token { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }
}
