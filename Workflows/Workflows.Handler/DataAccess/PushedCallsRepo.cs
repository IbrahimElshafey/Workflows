using Microsoft.EntityFrameworkCore;
using Workflows.Handler.Abstraction.Abstraction;
using Workflows.Handler.InOuts.Entities;

namespace Workflows.Handler.DataAccess;

internal class SignalsStore : ISignalsStore
{
    private readonly WaitsDataContext _context;

    public SignalsStore(WaitsDataContext context)
    {
        _context = context;
    }
    public async Task<SignalEntity> GetSignal(long signalId)
    {
        return await _context
            .Signals
            .FindAsync(signalId);
    }

    public Task SaveSignal(SignalEntity signal)
    {
        _context.Signals.Add(signal);
        return Task.CompletedTask;
    }

    public async Task<bool> IsSignalAlreadyMatchedToWorkflow(long signalId, int rootWorkflowId)
    {
        return await _context.
            MethodWaits.
            AsNoTracking().
            Where(x =>
                x.Status == Handler.InOuts.WaitStatus.Completed &&
                x.SignalId == signalId &&
                x.RootWorkflowId == rootWorkflowId)
            .AnyAsync();
    }
}