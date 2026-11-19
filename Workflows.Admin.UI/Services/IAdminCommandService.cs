namespace Workflows.Admin.UI.Services
{
    /// <summary>
    /// Admin command service for manual workflow lifecycle actions.
    /// Requires <see cref="Workflows.Abstraction.Orchestrator.IOrchestrator"/> to be registered in DI.
    /// </summary>
    public interface IAdminCommandService
    {
        bool CanExecuteActions { get; }

        Task<Guid> StartInstanceAsync(string workflowName, int version, object? input, CancellationToken ct = default);
        Task CancelInstanceAsync(Guid instanceId, string token, string? reason, CancellationToken ct = default);
        Task DispatchSignalAsync(Guid instanceId, string signalIdentifier, object? payload, CancellationToken ct = default);
        Task ForceCompensateAsync(Guid instanceId, string token, CancellationToken ct = default);
        Task TerminateInstanceAsync(Guid instanceId, CancellationToken ct = default);
    }
}
