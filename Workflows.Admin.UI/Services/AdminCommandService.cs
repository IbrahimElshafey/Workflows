using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Orchestrator;

namespace Workflows.Admin.UI.Services
{
    public class AdminCommandService : IAdminCommandService
    {
        private readonly IOrchestrator? _orchestrator;

        public AdminCommandService(IOrchestrator? orchestrator = null)
        {
            _orchestrator = orchestrator;
        }

        public bool CanExecuteActions => _orchestrator != null;

        public Task<Guid> StartInstanceAsync(string workflowName, int version, object? input, CancellationToken ct = default)
        {
            EnsureActionsEnabled();
            return _orchestrator!.StartWorkflowAsync(workflowName, version, input ?? new object());
        }

        public Task CancelInstanceAsync(Guid instanceId, string token, string? reason, CancellationToken ct = default)
        {
            EnsureActionsEnabled();
            return _orchestrator!.CancelWorkflowAsync(instanceId, token, reason ?? string.Empty);
        }

        public async Task DispatchSignalAsync(Guid instanceId, string signalIdentifier, object? payload, CancellationToken ct = default)
        {
            EnsureActionsEnabled();

            var signal = new SignalDto
            {
                Id = Guid.NewGuid(),
                SignalIdentifier = signalIdentifier,
                Data = payload ?? new object(),
                ClientSentTime = DateTime.UtcNow,
                OrchestratorReceiveTime = DateTime.UtcNow
            };

            await _orchestrator!.ProcessSignalAsync(signal);
        }

        public Task ForceCompensateAsync(Guid instanceId, string token, CancellationToken ct = default)
        {
            EnsureActionsEnabled();
            // Compensation is triggered by a cancellation token in this engine.
            return _orchestrator!.CancelWorkflowAsync(instanceId, token, "Force compensation triggered from admin UI");
        }

        public async Task TerminateInstanceAsync(Guid instanceId, CancellationToken ct = default)
        {
            EnsureActionsEnabled();
            // Termination is implemented as a forced cancellation using a reserved admin token.
            await _orchestrator!.CancelWorkflowAsync(instanceId, "admin-terminate", "Instance terminated from admin UI");
        }

        private void EnsureActionsEnabled()
        {
            if (_orchestrator == null)
            {
                throw new InvalidOperationException(
                    "Write actions are disabled because no IOrchestrator is registered. " +
                    "Register the workflow engine or set EnableWriteActions=false.");
            }
        }
    }
}
