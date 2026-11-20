using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Persistence;

namespace Workflows.Admin.UI.Services
{
    public class AdminCommandService : IAdminCommandService
    {
        private readonly IOrchestrator? _orchestrator;
        private readonly IWorkflowStore? _store;

        public AdminCommandService(IOrchestrator? orchestrator = null, IWorkflowStore? store = null)
        {
            _orchestrator = orchestrator;
            _store = store;
        }

        public bool CanExecuteActions => _orchestrator != null;

        public bool CanRetryCompensation => _store != null;

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

        public async Task<bool> RetryCompensationAsync(Guid instanceId, CancellationToken ct = default)
        {
            if (_store == null)
            {
                throw new InvalidOperationException(
                    "Compensation retries are disabled because no IWorkflowStore is registered.");
            }

            // The store side is idempotent: only instances stuck in CompensationFailed are reset, and the
            // transition back to InError is what requeues compensation (sweeper and/or outbox notification).
            return await _store.ResetCompensationAsync(instanceId, ct);
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
