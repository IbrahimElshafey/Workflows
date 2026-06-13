using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Orchestrator;

namespace Workflows.Hosting.InProcess
{
    public class InboxOrchestrator : IOrchestrator
    {
        private readonly Workflows.Orchestrator.Orchestrator _inner;
        private readonly CommandResultInboxWriter _inboxWriter;
        private readonly InboxOptions _options;

        public InboxOrchestrator(
            Workflows.Orchestrator.Orchestrator inner, 
            CommandResultInboxWriter inboxWriter,
            InboxOptions options)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _inboxWriter = inboxWriter ?? throw new ArgumentNullException(nameof(inboxWriter));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public Task ProcessCommandResultAsync(CommandResultDto commandResultDto)
        {
            if (commandResultDto == null) throw new ArgumentNullException(nameof(commandResultDto));
            if (_options.BypassInbox)
            {
                return _inner.ProcessCommandResultAsync(commandResultDto);
            }
            return _inboxWriter.WriteAsync(commandResultDto.CommandWaitId, commandResultDto.Result, true);
        }

        public Task ProcessSignalAsync(SignalDto signalDto)
        {
            return _inner.ProcessSignalAsync(signalDto);
        }

        public Task<Guid> StartWorkflowAsync(string workflowName, int version, object input)
        {
            return _inner.StartWorkflowAsync(workflowName, version, input);
        }
    }
}
