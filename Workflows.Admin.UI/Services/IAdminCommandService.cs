namespace Workflows.Admin.UI.Services
{
    /// <summary>
    /// Admin command service for manual workflow lifecycle actions.
    /// Lifecycle actions (start/cancel/signal/compensate/terminate) require
    /// <see cref="Workflows.Abstraction.Orchestrator.IOrchestrator"/> to be registered in DI.
    /// <see cref="RetryCompensationAsync"/> only requires the persistence store, so it also works in
    /// read-only orchestrator-less deployments.
    /// </summary>
    public interface IAdminCommandService
    {
        bool CanExecuteActions { get; }

        /// <summary>
        /// True when <see cref="RetryCompensationAsync"/> is available (an <c>IWorkflowStore</c> is registered).
        /// </summary>
        bool CanRetryCompensation { get; }

        Task<Guid> StartInstanceAsync(string workflowName, int version, object? input, CancellationToken ct = default);
        Task CancelInstanceAsync(Guid instanceId, string token, string? reason, CancellationToken ct = default);
        Task DispatchSignalAsync(Guid instanceId, string signalIdentifier, object? payload, CancellationToken ct = default);
        Task ForceCompensateAsync(Guid instanceId, string token, CancellationToken ct = default);
        Task TerminateInstanceAsync(Guid instanceId, CancellationToken ct = default);

        /// <summary>
        /// Requeues compensation for an instance stuck in <c>CompensationFailed</c>: compensation waits are
        /// reset (attempts cleared, status back to Waiting) and the instance returns to <c>InError</c> so the
        /// compensation worker picks it up again. Idempotent: returns <c>false</c> when the instance is not
        /// in <c>CompensationFailed</c> (for example on a second call), and never emits a duplicate DLQ entry.
        /// </summary>
        /// <returns><c>true</c> when a retry cycle was queued; <c>false</c> when nothing needed to be done.</returns>
        Task<bool> RetryCompensationAsync(Guid instanceId, CancellationToken ct = default);
    }
}