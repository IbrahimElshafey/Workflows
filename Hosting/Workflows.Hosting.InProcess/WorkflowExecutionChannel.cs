using System.Threading.Channels;

namespace Workflows.Hosting.InProcess
{
    public class WorkflowExecutionChannel
    {
        private readonly Channel<WorkflowRunContext> _ingressChannel;
        private readonly Channel<StateDelta> _egressChannel;

        public ChannelReader<WorkflowRunContext> IngressReader => _ingressChannel.Reader;
        public ChannelWriter<WorkflowRunContext> IngressWriter => _ingressChannel.Writer;

        public ChannelReader<StateDelta> EgressReader => _egressChannel.Reader;
        public ChannelWriter<StateDelta> EgressWriter => _egressChannel.Writer;

        public WorkflowExecutionChannel(int capacity = 1000)
        {
            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true, // We will use a single processing loop for sequence guarantees
                SingleWriter = false
            };

            _ingressChannel = Channel.CreateBounded<WorkflowRunContext>(options);
            _egressChannel = Channel.CreateBounded<StateDelta>(options);
        }
    }
}
